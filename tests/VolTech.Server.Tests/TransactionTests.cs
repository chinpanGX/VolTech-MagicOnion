using Grpc.Core;
using MagicOnion.Client;
using VolTech.Server.Tests.Support;

namespace VolTech.Server.Tests
{
    /// <summary>
    /// トランザクション・冪等性・ゲームログ。PostgreSQL を使う。
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public class TransactionTests
    {
        private readonly PostgresFixture database;

        public TransactionTests(PostgresFixture database)
        {
            this.database = database;
        }

        [Fact]
        public async Task SameRequestId_ReturnsSavedResponseWithoutRunningAgain()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<IPlayerTestService>(host.Channel);

            var first = await client.WithHeaders(AuthTests.Bearer(token, "req-1")).AddGemsAsync(100);
            var replayed = await client.WithHeaders(AuthTests.Bearer(token, "req-1")).AddGemsAsync(100);
            var next = await client.WithHeaders(AuthTests.Bearer(token, "req-2")).AddGemsAsync(100);

            Assert.Equal(100, first);
            Assert.Equal(100, replayed);
            Assert.Equal(200, next);
            var playerId = await PlayerIdAsync(host, token);
            Assert.Equal(2L, await database.QueryScalarAsync<long>("SELECT count(*) FROM game_logs WHERE player_id = $1", playerId));
        }

        [Fact]
        public async Task GameLog_IsWrittenWithRpcAndRequestId()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<IPlayerTestService>(host.Channel);

            await client.WithHeaders(AuthTests.Bearer(token, "log-1")).AddGemsAsync(30);

            var playerId = await PlayerIdAsync(host, token);
            var row = await database.QueryScalarAsync<string>(
                "SELECT concat_ws('|', request_id, rpc, kind, body::text) FROM game_logs WHERE player_id = $1", playerId);
            Assert.Equal(
                "log-1|IPlayerTestService/AddGemsAsync|gems|{\"note\": \"hello\", \"after\": 30, \"delta\": 30, \"reason\": \"test_add\"}",
                row);
        }

        [Fact]
        public async Task MissingRequestId_IsInvalidArgument()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<IPlayerTestService>(host.Channel);

            var missing = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token)).AddGemsAsync(1));
            var invalid = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "not_valid!")).AddGemsAsync(1));

            Assert.Equal(StatusCode.InvalidArgument, missing.StatusCode);
            Assert.Equal(StatusCode.InvalidArgument, invalid.StatusCode);
        }

        [Fact]
        public async Task RequestIdReusedForAnotherRpc_IsInvalidArgument()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<IPlayerTestService>(host.Channel).WithHeaders(AuthTests.Bearer(token, "shared-1"));

            await client.AddGemsAsync(1);
            var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.AddGemsThenFailAsync(1));

            Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        }

        [Fact]
        public async Task BusinessError_RollsBackDataAndGameLogs()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<IPlayerTestService>(host.Channel);

            await client.WithHeaders(AuthTests.Bearer(token, "ok-1")).AddGemsAsync(10);
            var failing = client.WithHeaders(AuthTests.Bearer(token, "fail-1"));
            var first = await Assert.ThrowsAsync<RpcException>(async () => await failing.AddGemsThenFailAsync(5));
            // エラーのレスポンスは保存しないので、再送するともう一度処理して同じエラーになる
            var retried = await Assert.ThrowsAsync<RpcException>(async () => await failing.AddGemsThenFailAsync(5));

            Assert.Equal(StatusCode.FailedPrecondition, first.StatusCode);
            Assert.Equal(StatusCode.FailedPrecondition, retried.StatusCode);
            var playerId = await PlayerIdAsync(host, token);
            Assert.Equal(10, await database.QueryScalarAsync<int>("SELECT amount FROM test_gems WHERE player_id = $1", playerId));
            Assert.Equal(0L, await database.QueryScalarAsync<long>(
                "SELECT count(*) FROM game_logs WHERE player_id = $1 AND request_id = 'fail-1'", playerId));
        }

        [Fact]
        public async Task NoSideEffectsRpc_CannotWrite()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<IPlayerTestService>(host.Channel).WithHeaders(AuthTests.Bearer(token));

            var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.WriteInReadOnlyAsync());

            Assert.Equal(StatusCode.Internal, exception.StatusCode);
        }

        private static async Task<string> PlayerIdAsync(TestHost host, string token)
        {
            var whoAmI = await MagicOnionClient.Create<IPlayerTestService>(host.Channel).WithHeaders(AuthTests.Bearer(token)).WhoAmIAsync();
            return Ulid.Parse(whoAmI.Split(' ')[0]).ToString();
        }
    }
}
