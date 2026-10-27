using Npgsql;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// ITransactions.RunAsync に渡す処理が受け取る、トランザクションとゲームログの書き込み口。
    /// </summary>
    /// <remarks>
    /// 作れるのは基盤(RunAsync)だけ。書き込み系の処理はこれを引数に要求し、冪等性の仕組みを通さない書き込みを防ぐ。
    /// 1 リクエストの中で並列に使わない(DB の接続とトランザクションは並列に使えない)。await を順に並べる。
    /// </remarks>
    public sealed class TxContext
    {
        private readonly List<GameLog> gameLogs = new();

        internal TxContext(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Ulid? playerId,
            bool isReadOnly,
            CancellationToken cancellationToken)
        {
            Connection = connection;
            Transaction = transaction;
            PlayerId = playerId;
            IsReadOnly = isReadOnly;
            CancellationToken = cancellationToken;
        }

        /// <summary>処理を動かしている接続。コマンドには <see cref="Transaction"/> も渡す。</summary>
        public NpgsqlConnection Connection { get; }

        /// <summary>処理を動かしているトランザクション。コミットとロールバックは基盤が行う。</summary>
        public NpgsqlTransaction Transaction { get; }

        /// <summary>認証したプレイヤー。[SkipAuth] の RPC では null。</summary>
        public Ulid? PlayerId { get; }

        /// <summary>読み取り専用のトランザクションか([NoSideEffects] の RPC)。</summary>
        public bool IsReadOnly { get; }

        /// <summary>リクエストのキャンセル(クライアントの切断・締め切り切れ)。DB の呼び出しに渡す。</summary>
        public CancellationToken CancellationToken { get; }

        internal IReadOnlyList<GameLog> GameLogs => gameLogs;

        /// <summary>
        /// 接続とトランザクションを設定したコマンドを作る。
        /// </summary>
        public NpgsqlCommand CreateCommand(string sql)
        {
            return new NpgsqlCommand(sql, Connection, Transaction);
        }

        /// <summary>
        /// 認証したプレイヤーのゲームログを書く。コミットするときに、同じトランザクションで game_logs に書く。
        /// ロールバックしたときと、再送で保存済みのレスポンスを返したときには残らない。
        /// </summary>
        /// <exception cref="InvalidOperationException">[NoSideEffects] の RPC か、認証していない RPC で呼んだとき。</exception>
        public void WriteGameLog(GameLog log)
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("[NoSideEffects] の RPC ではゲームログを書けない。");
            }

            if (PlayerId == null)
            {
                throw new InvalidOperationException("ゲームログには認証したプレイヤーが要る。[SkipAuth] の RPC では書けない。");
            }

            gameLogs.Add(log);
        }
    }
}
