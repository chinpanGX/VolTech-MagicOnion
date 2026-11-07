using System.Collections.Concurrent;
using MagicOnion;
using MagicOnion.Server;
using Npgsql;
using VolTech.Sandbox.E2E;
using VolTech.Server;
using VolTech.Server.Abstractions;
using VolTech.Server.Auth;
using VolTech.Server.Transaction;

namespace VolTech.E2E.Server
{
    /// <summary>
    /// 登録も作成も無いテスト用なので、プレイヤーはいつも作られている扱いにする。
    /// </summary>
    public sealed class ActivePlayerResolver : IPlayerResolver
    {
        public ValueTask<PlayerStatus> ResolveAsync(Ulid playerId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(PlayerStatus.Active);
        }
    }

    /// <summary>
    /// 本体を実行した回数など、RPC をまたいで覚える値。
    /// </summary>
    public sealed class E2EState
    {
        private readonly ConcurrentDictionary<string, int> bodyRuns = new();
        private readonly ConcurrentDictionary<string, bool> lostResponses = new();
        private int cancelledSlowCalls;

        public int CancelledSlowCalls => Volatile.Read(ref cancelledSlowCalls);

        public int CountBodyRun(string key)
        {
            return bodyRuns.AddOrUpdate(key, 1, static (_, count) => count + 1);
        }

        /// <summary>key で初めて呼ばれたら true。</summary>
        public bool FirstLoss(string key)
        {
            return lostResponses.TryAdd(key, true);
        }

        public void CountCancelledSlowCall()
        {
            Interlocked.Increment(ref cancelledSlowCalls);
        }
    }

    /// <summary>
    /// 共通レスポンス項目を集めるコレクター。
    /// </summary>
    public sealed class E2ECollector : ICommonResponseCollector<E2ECommon>
    {
        public string? Note { get; set; }

        public ValueTask<E2ECommon?> BuildAsync(TxContext tx)
        {
            return ValueTask.FromResult(Note == null ? null : new E2ECommon { Note = Note });
        }
    }

    public sealed class E2EService : ServiceBase<IE2EService>, IE2EService
    {
        private readonly ITransactions transactions;
        private readonly NpgsqlDataSource dataSource;
        private readonly E2EState state;

        public E2EService(ITransactions transactions, NpgsqlDataSource dataSource, E2EState state)
        {
            this.transactions = transactions;
            this.dataSource = dataSource;
            this.state = state;
        }

        public async UnaryResult<E2EReply> EchoAsync(string key)
        {
            return await transactions.RunAsync(Context, tx => Task.FromResult(CreateReply(tx, key, "echo:" + key)));
        }

        public async UnaryResult<E2EReply> ReadAsync(string key)
        {
            return await transactions.RunAsync(Context, tx => Task.FromResult(CreateReply(tx, key, null)));
        }

        public async UnaryResult<E2EReply> PublicAsync(string key)
        {
            return await transactions.RunAsync(Context, tx => Task.FromResult(CreateReply(tx, key, null)));
        }

        public async UnaryResult<E2EReply> LoseResponseOnceAsync(string key)
        {
            var reply = await transactions.RunAsync(Context, tx => Task.FromResult(CreateReply(tx, key, null)));
            if (state.FirstLoss(key))
            {
                // 処理はコミット済み。応答が失われたのと同じ
                throw new InvalidOperationException("lost response");
            }

            return reply;
        }

        public UnaryResult<E2EReply> ThrowBusinessAsync()
        {
            throw new BusinessException(
                E2EErrors.NotEnough,
                "not enough",
                new Dictionary<string, string> { [E2EErrors.MetadataKey] = "300" });
        }

        public UnaryResult<E2EReply> ThrowUnexpectedAsync()
        {
            throw new InvalidOperationException("secret detail that must not reach the client");
        }

        public UnaryResult<E2EReply> WithoutRunAsync()
        {
            return UnaryResult.FromResult(new E2EReply());
        }

        public async UnaryResult<bool> ExpireTokenAsync()
        {
            await using var command = dataSource.CreateCommand(
                "DELETE FROM access_tokens WHERE device_id IN (SELECT device_id FROM devices WHERE player_id = $1)");
            command.Parameters.AddWithValue(Context.GetAuthContext().PlayerId.ToString());
            await command.ExecuteNonQueryAsync(Context.CallContext.CancellationToken);
            return true;
        }

        public async UnaryResult<bool> SlowAsync(int milliseconds)
        {
            try
            {
                await Task.Delay(milliseconds, Context.CallContext.CancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                state.CountCancelledSlowCall();
                throw;
            }
        }

        public UnaryResult<int> CancelledSlowCallsAsync()
        {
            return UnaryResult.FromResult(state.CancelledSlowCalls);
        }

        private E2EReply CreateReply(TxContext tx, string key, string? note)
        {
            if (note != null)
            {
                tx.GetCollector<E2ECollector>().Note = note;
            }

            var headers = Context.CallContext.RequestHeaders;
            return new E2EReply
            {
                PlayerId = Context.FindAuthContext()?.PlayerId.ToString() ?? "",
                RequestId = headers.GetValue(ApiHeaders.RequestId) ?? "",
                GameDate = headers.GetValue(ApiHeaders.GameDate) ?? "",
                AppVersion = headers.GetValue(ApiHeaders.AppVersion) ?? "",
                BodyRuns = state.CountBodyRun(key),
            };
        }
    }
}
