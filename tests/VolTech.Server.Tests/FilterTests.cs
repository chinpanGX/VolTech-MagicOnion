using Google.Rpc;
using Grpc.Core;
using MagicOnion.Client;
using VolTech.Server.Abstractions;
using VolTech.Server.Tests.Support;

namespace VolTech.Server.Tests
{
    /// <summary>
    /// DB を使わずに確かめられる、フィルターの振る舞い(エラーの形・レスポンスヘッダー・順序)。
    /// </summary>
    public class FilterTests
    {
        [Fact]
        public async Task BusinessException_ReachesClientAsFailedPreconditionWithErrorInfo()
        {
            await using var host = await TestHost.StartAsync();
            var client = MagicOnionClient.Create<IPublicTestService>(host.Channel);

            var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.ThrowBusinessAsync());

            Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
            Assert.NotNull(exception.Trailers.Get(ApiHeaders.ErrorDetailsTrailer));
            var info = exception.GetRpcStatus()?.GetDetail<ErrorInfo>();
            Assert.NotNull(info);
            Assert.Equal("test", info.Domain);
            Assert.Equal("NOT_ENOUGH_GEMS", info.Reason);
            Assert.Equal("300", info.Metadata["required"]);
        }

        [Fact]
        public async Task UnexpectedException_BecomesInternalWithoutDetail()
        {
            await using var host = await TestHost.StartAsync();
            var client = MagicOnionClient.Create<IPublicTestService>(host.Channel);

            var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.ThrowUnexpectedAsync());

            Assert.Equal(StatusCode.Internal, exception.StatusCode);
            Assert.DoesNotContain("secret detail", exception.Status.Detail);
            var info = exception.GetRpcStatus()?.GetDetail<ErrorInfo>();
            Assert.NotNull(info);
            Assert.Equal(ReservedErrors.Domain, info.Domain);
            Assert.Equal("INTERNAL_ERROR", info.Reason);
        }

        [Fact]
        public async Task ResponseHeaders_HaveServerTimeAndGameDate()
        {
            // 2026-10-06 03:00 JST は、4 時に日付が変わるので 10-05
            var now = new DateTimeOffset(2026, 10, 6, 3, 0, 0, TimeSpan.FromHours(9));
            await using var host = await TestHost.StartAsync(now: now);
            var client = MagicOnionClient.Create<IPublicTestService>(host.Channel);

            var call = client.GetGameDateAsync();
            var headers = await call.ResponseHeadersAsync;
            var body = await call;

            Assert.Equal(now.ToUnixTimeMilliseconds().ToString(), headers.GetValue(ApiHeaders.ServerTime));
            Assert.Equal("2026-10-05", headers.GetValue(ApiHeaders.GameDate));
            Assert.Equal("2026-10-05", body);
        }

        [Fact]
        public async Task ErrorResponse_AlsoHasResponseHeaders()
        {
            await using var host = await TestHost.StartAsync();
            var client = MagicOnionClient.Create<IPublicTestService>(host.Channel);

            var call = client.ThrowBusinessAsync();
            var headers = await call.ResponseHeadersAsync;

            Assert.NotNull(headers.GetValue(ApiHeaders.ServerTime));
            Assert.NotNull(headers.GetValue(ApiHeaders.GameDate));
            await Assert.ThrowsAsync<RpcException>(async () => await call);
        }

        [Fact]
        public async Task AppFilter_RunsInsideFoundationFilters()
        {
            await using var host = await TestHost.StartAsync();
            var client = MagicOnionClient.Create<IPublicTestService>(host.Channel);

            var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.ThrowFromAppFilterAsync());

            // RpcLogFilter が外側で変換し、ServerTimeFilter が先に日付を決めている
            Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
            var info = exception.GetRpcStatus()?.GetDetail<ErrorInfo>();
            Assert.NotNull(info);
            Assert.Equal("2026-10-06", info.Metadata["today"]);
        }

        [Fact]
        public async Task MissingToken_IsUnauthenticated()
        {
            await using var host = await TestHost.StartAsync();
            var client = MagicOnionClient.Create<IPlayerTestService>(host.Channel);

            var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.WhoAmIAsync());

            Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
        }
    }
}
