using MagicOnion;
using MagicOnion.Server;
using VolTech.Server.Abstractions;
using VolTech.Server.Transaction;

namespace VolTech.Server.Tests.Support
{
    /// <summary>
    /// 認証の要らない、テスト用のサービス。DB を使わない。
    /// </summary>
    [SkipAuth]
    public interface IPublicTestService : IService<IPublicTestService>
    {
        UnaryResult<string> GetGameDateAsync();

        UnaryResult ThrowBusinessAsync();

        UnaryResult ThrowUnexpectedAsync();

        /// <summary>アプリが付けたフィルター(Order の既定)が業務エラーを投げる。基盤のフィルターより内側で動くかを見る。</summary>
        UnaryResult ThrowFromAppFilterAsync();
    }

    /// <summary>
    /// 認証の要る、テスト用のサービス。test_gems テーブルを使う。
    /// </summary>
    public interface IPlayerTestService : IService<IPlayerTestService>
    {
        [NoSideEffects]
        UnaryResult<string> WhoAmIAsync();

        /// <summary>ジェムを足して、足した後の数を返す。ゲームログを 1 件書く。冪等性の対象。</summary>
        UnaryResult<int> AddGemsAsync(int amount);

        /// <summary>ジェムを足してゲームログを書いてから、業務エラーを投げる。何も残らないはず。</summary>
        UnaryResult<int> AddGemsThenFailAsync(int amount);

        /// <summary>[NoSideEffects] なのに書き込む。読み取り専用のトランザクションなので失敗するはず。</summary>
        [NoSideEffects]
        UnaryResult<int> WriteInReadOnlyAsync();
    }

    public static class TestErrors
    {
        public static readonly ErrorCode NotEnoughGems = new ErrorCode("test", "NOT_ENOUGH_GEMS");
    }

    public sealed class PublicTestService : ServiceBase<IPublicTestService>, IPublicTestService
    {
        public UnaryResult<string> GetGameDateAsync()
        {
            return UnaryResult.FromResult(Context.GetGameDate().ToString("yyyy-MM-dd"));
        }

        public UnaryResult ThrowBusinessAsync()
        {
            throw new BusinessException(
                TestErrors.NotEnoughGems,
                "not enough gems",
                new Dictionary<string, string> { ["required"] = "300" });
        }

        public UnaryResult ThrowUnexpectedAsync()
        {
            throw new InvalidOperationException("secret detail that must not reach the client");
        }

        [ThrowBusinessFilter]
        public UnaryResult ThrowFromAppFilterAsync()
        {
            return UnaryResult.CompletedResult;
        }
    }

    /// <summary>
    /// アプリがメソッドに付けるフィルターの例。Order は既定のまま。
    /// </summary>
    public sealed class ThrowBusinessFilter : MagicOnionFilterAttribute
    {
        public override ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
        {
            // 基盤の ServerTimeFilter が先に動いていれば、日付が取れる
            var today = context.GetGameDate();
            throw new BusinessException(
                TestErrors.NotEnoughGems,
                "from app filter",
                new Dictionary<string, string> { ["today"] = today.ToString("yyyy-MM-dd") });
        }
    }

    public sealed class PlayerTestService : ServiceBase<IPlayerTestService>, IPlayerTestService
    {
        private readonly ITransactions transactions;

        public PlayerTestService(ITransactions transactions)
        {
            this.transactions = transactions;
        }

        public UnaryResult<string> WhoAmIAsync()
        {
            var auth = Context.GetAuthContext();
            return UnaryResult.FromResult($"{auth.PlayerId} created={auth.PlayerCreated}");
        }

        public async UnaryResult<int> AddGemsAsync(int amount)
        {
            return await transactions.RunAsync(Context, async tx =>
            {
                var after = await AddAsync(tx, amount);
                tx.WriteGameLog(GameLog.Quantity("gems", amount, after, "test_add").Field("note", "hello"));
                return after;
            });
        }

        public async UnaryResult<int> AddGemsThenFailAsync(int amount)
        {
            return await transactions.RunAsync<int>(Context, async tx =>
            {
                var after = await AddAsync(tx, amount);
                tx.WriteGameLog(GameLog.Quantity("gems", amount, after, "test_fail"));
                throw new BusinessException(TestErrors.NotEnoughGems, "fail after write");
            });
        }

        public async UnaryResult<int> WriteInReadOnlyAsync()
        {
            return await transactions.RunAsync(Context, tx => AddAsync(tx, 1));
        }

        private static async Task<int> AddAsync(TxContext tx, int amount)
        {
            await using var command = tx.CreateCommand(
                "INSERT INTO test_gems (player_id, amount) VALUES ($1, $2) "
                + "ON CONFLICT (player_id) DO UPDATE SET amount = test_gems.amount + excluded.amount RETURNING amount");
            command.Parameters.AddWithValue(tx.PlayerId!.Value.ToGuid());
            command.Parameters.AddWithValue(amount);
            return (int)(await command.ExecuteScalarAsync(tx.CancellationToken))!;
        }
    }
}
