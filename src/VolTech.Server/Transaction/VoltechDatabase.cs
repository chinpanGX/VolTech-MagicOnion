using DbUp;
using DbUp.Engine.Output;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// 基盤のテーブル(devices・access_tokens・last_responses・game_logs)を作成・更新する。
    /// </summary>
    public static class VoltechDatabase
    {
        /// <summary>
        /// 適用済みの記録を入れるテーブル。アプリのマイグレーションと同じテーブルにすると、
        /// 互いに相手の分を「知らないマイグレーション」として扱うので分ける。
        /// </summary>
        public const string JournalTable = "_voltech_migrations";

        /// <summary>
        /// まだ適用していないマイグレーションを適用する。アプリの起動時に、アプリのマイグレーションより先に呼ぶ。
        /// </summary>
        /// <exception cref="InvalidOperationException">適用に失敗したとき。</exception>
        public static void Migrate(string connectionString)
        {
            var upgrader = DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(VoltechDatabase).Assembly,
                    name => name.Contains(".Transaction.Migrations.", StringComparison.Ordinal))
                .JournalToPostgresqlTable("public", JournalTable)
                .WithTransactionPerScript()
                .LogTo(new NoOpUpgradeLog())
                .Build();

            var result = upgrader.PerformUpgrade();
            if (!result.Successful)
            {
                throw new InvalidOperationException(
                    $"基盤のマイグレーション {result.ErrorScript?.Name} に失敗した。", result.Error);
            }
        }
    }
}
