using Npgsql;
using Testcontainers.PostgreSql;
using VolTech.Server.Transaction;

namespace VolTech.Server.Tests.Support
{
    /// <summary>
    /// PostgreSQL をコンテナで 1 つ起動し、基盤のマイグレーションとテスト用のテーブルを入れる。Docker が要る。
    /// </summary>
    public sealed class PostgresFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").Build();

        public string ConnectionString => container.GetConnectionString();

        public async Task InitializeAsync()
        {
            await container.StartAsync();
            VoltechDatabase.Migrate(ConnectionString);
            // 2 回目は何もしない
            VoltechDatabase.Migrate(ConnectionString);

            await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
            await using var command = dataSource.CreateCommand(
                "CREATE TABLE test_gems (player_id uuid PRIMARY KEY, amount int NOT NULL)");
            await command.ExecuteNonQueryAsync();
        }

        public async Task DisposeAsync()
        {
            await container.DisposeAsync();
        }

        public async Task<T?> QueryScalarAsync<T>(string sql, params object[] parameters)
        {
            await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
            await using var command = dataSource.CreateCommand(sql);
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter);
            }

            var value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? default : (T)value;
        }
    }

    [CollectionDefinition(Name)]
    public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
    {
        public const string Name = "postgres";
    }
}
