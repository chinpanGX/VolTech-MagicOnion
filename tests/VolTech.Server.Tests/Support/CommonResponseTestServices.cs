using MagicOnion;
using MagicOnion.Server;
using MessagePack;
using VolTech.Server.Abstractions;
using VolTech.Server.Transaction;

namespace VolTech.Server.Tests.Support
{
    /// <summary>
    /// テスト用の共通レスポンス項目。アプリの TCommon に当たる。
    /// </summary>
    [MessagePackObject]
    public sealed class TestCommon
    {
        /// <summary>player_diff に当たる。ジェムの変更後の数。</summary>
        [Key(0)]
        public int? Gems { get; set; }

        /// <summary>バッジに当たる。トランザクションの中で DB から計算する。</summary>
        [Key(1)]
        public int? GemBadge { get; set; }

        /// <summary>通知に当たる。</summary>
        [Key(2)]
        public string[]? Notifications { get; set; }

        /// <summary>コレクターがコンストラクターで DI の部品を受け取れたか(TimeProvider の今の時刻)。</summary>
        [Key(3)]
        public long CollectedAt { get; set; }
    }

    [MessagePackObject]
    public sealed class GemsResult : IApiResponse<TestCommon>
    {
        [Key(0)]
        public TestCommon? Common { get; set; }

        [Key(1)]
        public int Amount { get; set; }

        /// <summary>本体を呼んだ回数。再送で本体を呼ばないことを見る。</summary>
        [Key(2)]
        public int Runs { get; set; }
    }

    /// <summary>登録したものと違う TCommon。</summary>
    [MessagePackObject]
    public sealed class OtherCommon
    {
        [Key(0)]
        public int Value { get; set; }
    }

    [MessagePackObject]
    public sealed class OtherResult : IApiResponse<OtherCommon>
    {
        [Key(0)]
        public OtherCommon? Common { get; set; }
    }

    /// <summary>
    /// テスト用のコレクター。書き込み関数が記録し、BuildAsync で TestCommon を作る。
    /// </summary>
    public sealed class TestCollector : ICommonResponseCollector<TestCommon>
    {
        private readonly TimeProvider time;
        private readonly List<string> notifications = new();
        private int? gems;
        private bool badge;

        public TestCollector(TimeProvider time)
        {
            this.time = time;
        }

        public void SetGems(int after)
        {
            gems = after;
        }

        public void Notify(string message)
        {
            notifications.Add(message);
        }

        public void MarkBadge()
        {
            badge = true;
        }

        public async ValueTask<TestCommon?> BuildAsync(TxContext tx)
        {
            if (gems == null && !badge && notifications.Count == 0)
            {
                return null;
            }

            int? gemBadge = null;
            if (badge)
            {
                await using var command = tx.CreateCommand("SELECT coalesce((SELECT amount FROM test_gems WHERE player_id = $1), 0)");
                command.Parameters.AddWithValue(tx.PlayerId!.Value.ToString());
                gemBadge = (int)(await command.ExecuteScalarAsync(tx.CancellationToken))!;
            }

            return new TestCommon
            {
                Gems = gems,
                GemBadge = gemBadge,
                Notifications = notifications.Count == 0 ? null : notifications.ToArray(),
                CollectedAt = time.GetUtcNow().ToUnixTimeSeconds(),
            };
        }
    }

    /// <summary>
    /// 共通レスポンス項目を返す、テスト用のサービス。test_gems テーブルを使う。
    /// </summary>
    public interface ICommonTestService : IService<ICommonTestService>
    {
        /// <summary>ジェムを足し、player_diff と通知を記録する。冪等性の対象。</summary>
        UnaryResult<GemsResult> AddGemsAsync(int amount);

        /// <summary>ジェムを足して記録してから、業務エラーを投げる。</summary>
        UnaryResult<GemsResult> AddGemsThenFailAsync(int amount);

        /// <summary>何も記録しない。Common は null のはず。</summary>
        UnaryResult<GemsResult> NoChangeAsync();

        /// <summary>読み取り専用のトランザクションで、バッジだけを計算する。</summary>
        [NoSideEffects]
        UnaryResult<GemsResult> GetHomeAsync();

        /// <summary>登録したものと違う TCommon のレスポンスを返す。</summary>
        UnaryResult<OtherResult> WrongCommonAsync();

        /// <summary>登録したものと違う型でコレクターを取り出す。</summary>
        UnaryResult<GemsResult> WrongCollectorAsync();
    }

    public sealed class CommonTestService : ServiceBase<ICommonTestService>, ICommonTestService
    {
        private static int runs;
        private readonly ITransactions transactions;

        public CommonTestService(ITransactions transactions)
        {
            this.transactions = transactions;
        }

        public async UnaryResult<GemsResult> AddGemsAsync(int amount)
        {
            return await transactions.RunAsync(Context, async tx =>
            {
                var after = await AddAsync(tx, amount);
                var collector = tx.GetCollector<TestCollector>();
                collector.SetGems(after);
                collector.Notify($"added {amount}");
                return new GemsResult { Amount = after, Runs = Interlocked.Increment(ref runs) };
            });
        }

        public async UnaryResult<GemsResult> AddGemsThenFailAsync(int amount)
        {
            return await transactions.RunAsync<GemsResult>(Context, async tx =>
            {
                var after = await AddAsync(tx, amount);
                tx.GetCollector<TestCollector>().SetGems(after);
                throw new BusinessException(TestErrors.NotEnoughGems, "fail after collect");
            });
        }

        public async UnaryResult<GemsResult> NoChangeAsync()
        {
            return await transactions.RunAsync(Context, tx => Task.FromResult(new GemsResult()));
        }

        public async UnaryResult<GemsResult> GetHomeAsync()
        {
            return await transactions.RunAsync(Context, tx =>
            {
                tx.GetCollector<TestCollector>().MarkBadge();
                return Task.FromResult(new GemsResult());
            });
        }

        public async UnaryResult<OtherResult> WrongCommonAsync()
        {
            return await transactions.RunAsync(Context, tx => Task.FromResult(new OtherResult()));
        }

        public async UnaryResult<GemsResult> WrongCollectorAsync()
        {
            return await transactions.RunAsync(Context, tx =>
            {
                tx.GetCollector<object>();
                return Task.FromResult(new GemsResult());
            });
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
}
