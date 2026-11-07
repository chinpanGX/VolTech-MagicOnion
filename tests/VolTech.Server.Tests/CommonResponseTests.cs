using Grpc.Core;
using MagicOnion.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VolTech.Server.Hosting;
using VolTech.Server.Tests.Support;

namespace VolTech.Server.Tests
{
    /// <summary>
    /// 共通レスポンス項目(docs/spec/common-response.md)。PostgreSQL を使う。
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public class CommonResponseTests
    {
        private readonly PostgresFixture database;

        public CommonResponseTests(PostgresFixture database)
        {
            this.database = database;
        }

        [Fact]
        public async Task Collected_IsFilledIntoCommon()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var result = await client.WithHeaders(AuthTests.Bearer(token, "common-1")).AddGemsAsync(40);

            Assert.NotNull(result.Common);
            Assert.Equal(40, result.Common!.Gems);
            Assert.Equal(new[] { "added 40" }, result.Common.Notifications);
            Assert.Null(result.Common.GemBadge);
            // コレクターはリクエストの ServiceProvider から作られ、DI の部品(TimeProvider)を受け取れる
            Assert.Equal(host.Time.GetUtcNow().ToUnixTimeSeconds(), result.Common.CollectedAt);
        }

        [Fact]
        public async Task SameRequestId_ReturnsSavedCommonWithoutRunningAgain()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var first = await client.WithHeaders(AuthTests.Bearer(token, "common-r")).AddGemsAsync(5);
            var replayed = await client.WithHeaders(AuthTests.Bearer(token, "common-r")).AddGemsAsync(5);

            Assert.Equal(first.Runs, replayed.Runs);
            Assert.Equal(5, replayed.Common!.Gems);
            Assert.Equal(new[] { "added 5" }, replayed.Common.Notifications);
        }

        /// <summary>RunAsync の外で作ったレスポンスは Common が詰まっていないので、検査が INTERNAL にする。</summary>
        [Fact]
        public async Task ResponseMadeOutsideRunAsync_IsInternal()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var exception = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-o")).WrapOutsideRunAsync(1));

            Assert.Equal(StatusCode.Internal, exception.StatusCode);
        }

        /// <summary>RunAsync を通らないレスポンスも、検査が INTERNAL にする。</summary>
        [Fact]
        public async Task ResponseWithoutRunAsync_IsInternal()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var exception = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-w")).WithoutRunAsync());

            Assert.Equal(StatusCode.Internal, exception.StatusCode);
        }

        /// <summary>保存済みのレスポンスを読めない再送は INTERNAL になり、本体を呼ばない。</summary>
        [Fact]
        public async Task UnreadableSavedResponse_IsInternalAndDoesNotRunAgain()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);
            var first = await client.WithHeaders(AuthTests.Bearer(token, "common-u")).AddGemsAsync(3);
            await using (var connection = new NpgsqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("UPDATE last_responses SET response = $1 WHERE request_id = 'common-u'", connection);
                // 配列ではなく文字列の MessagePack。GemsResult として読めない
                command.Parameters.AddWithValue(new byte[] { 0xA1, 0x61 });
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-u")).AddGemsAsync(3));

            Assert.Equal(StatusCode.Internal, exception.StatusCode);
            var next = await client.WithHeaders(AuthTests.Bearer(token, "common-v")).AddGemsAsync(0);
            Assert.Equal(first.Amount, next.Amount);
        }

        [Fact]
        public async Task NothingCollected_CommonIsNull()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var result = await client.WithHeaders(AuthTests.Bearer(token, "common-n")).NoChangeAsync();

            Assert.Null(result.Common);
        }

        [Fact]
        public async Task BusinessError_DiscardsCollectedAndRollsBack()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var exception = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-f")).AddGemsThenFailAsync(7));
            // 次のトランザクションのコレクターは新しく作られるので、失敗した分は残らない
            var next = await client.WithHeaders(AuthTests.Bearer(token, "common-f2")).NoChangeAsync();

            Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
            Assert.Null(next.Common);
        }

        [Fact]
        public async Task NoSideEffectsRpc_FillsCommonInReadOnlyTransaction()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            await client.WithHeaders(AuthTests.Bearer(token, "common-h")).AddGemsAsync(12);
            var home = await client.WithHeaders(AuthTests.Bearer(token)).GetHomeAsync();

            Assert.Equal(12, home.Common!.GemBadge);
            Assert.Null(home.Common.Gems);
        }

        [Fact]
        public async Task NotRegistered_IsInternal()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString, commonResponse: false);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var collect = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-u1")).AddGemsAsync(1));
            // 本体が何も記録しなくても、IApiResponse<> を実装したレスポンスなら設定の誤りとして止める
            var noChange = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-u2")).NoChangeAsync());

            Assert.Equal(StatusCode.Internal, collect.StatusCode);
            Assert.Equal(StatusCode.Internal, noChange.StatusCode);
        }

        [Fact]
        public async Task OtherCommonType_IsInternal()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await AuthTests.IssueTokenAsync(host);
            var client = MagicOnionClient.Create<ICommonTestService>(host.Channel);

            var wrongCommon = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-w1")).WrongCommonAsync());
            var wrongCollector = await Assert.ThrowsAsync<RpcException>(
                async () => await client.WithHeaders(AuthTests.Bearer(token, "common-w2")).WrongCollectorAsync());

            Assert.Equal(StatusCode.Internal, wrongCommon.StatusCode);
            Assert.Equal(StatusCode.Internal, wrongCollector.StatusCode);
        }

        [Fact]
        public void AddCommonResponse_Twice_Throws()
        {
            var builder = new ServiceCollection().AddVoltechServer();
            builder.AddCommonResponse<TestCommon, TestCollector>();

            Assert.Throws<InvalidOperationException>(() => builder.AddCommonResponse<TestCommon, TestCollector>());
        }
    }
}
