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
            command.Parameters.AddWithValue(tx.PlayerId!.Value.ToString());
            command.Parameters.AddWithValue(amount);
            return (int)(await command.ExecuteScalarAsync(tx.CancellationToken))!;
        }
    }

    /// <summary>
    /// RpcSettings が属性を読む場所を確かめる、テスト用のサービス。認証が要るので、呼ばずに RpcSettings.Create で使う。
    /// </summary>
    public interface IAttributePlacementService : IService<IAttributePlacementService>
    {
        [SkipAuth]
        UnaryResult SkipAuthOnInterfaceMethodAsync();

        UnaryResult AttributesOnImplementationAsync();

        UnaryResult PlainAsync();
    }

    /// <summary>共有インターフェースではないインターフェース。同じシグネチャのメソッドに属性を付けてある。</summary>
    public interface IAttributeDecoy
    {
        [SkipAuth]
        [NoSideEffects]
        UnaryResult PlainAsync();
    }

    public sealed class AttributePlacementService : ServiceBase<IAttributePlacementService>, IAttributeDecoy, IAttributePlacementService
    {
        public UnaryResult SkipAuthOnInterfaceMethodAsync()
        {
            return UnaryResult.CompletedResult;
        }

        [SkipAuth]
        [NoSideEffects]
        public UnaryResult AttributesOnImplementationAsync()
        {
            return UnaryResult.CompletedResult;
        }

        public UnaryResult PlainAsync()
        {
            return UnaryResult.CompletedResult;
        }
    }

    /// <summary>実装を基底クラスに置くサービス。</summary>
    public interface IDerivedPlacementService : IService<IDerivedPlacementService>
    {
        [NoSideEffects]
        UnaryResult<int> ReadAsync();

        /// <summary>認証の要らない RPC。TestHost から呼んで、基底クラスに置いた実装でも属性が効くかを見る。</summary>
        [SkipAuth]
        UnaryResult<string> PingAsync();
    }

    public abstract class PlacementServiceBase : ServiceBase<IDerivedPlacementService>, IDerivedPlacementService
    {
        public UnaryResult<int> ReadAsync()
        {
            return UnaryResult.FromResult(0);
        }

        public UnaryResult<string> PingAsync()
        {
            return UnaryResult.FromResult("pong");
        }
    }

    public sealed class DerivedPlacementService : PlacementServiceBase
    {
    }

    /// <summary>共有インターフェースの基底インターフェース。ここに書いたメソッドには対応しない。</summary>
    public interface IInheritedMethodBase
    {
        [SkipAuth]
        [NoSideEffects]
        UnaryResult InheritedAsync();
    }

    /// <summary>基底インターフェースからメソッドを受け継ぐ共有インターフェース。</summary>
    public interface IInheritedMethodService : IService<IInheritedMethodService>, IInheritedMethodBase
    {
    }

    public sealed class InheritedMethodService : ServiceBase<IInheritedMethodService>, IInheritedMethodService
    {
        public UnaryResult InheritedAsync()
        {
            return UnaryResult.CompletedResult;
        }
    }
}
