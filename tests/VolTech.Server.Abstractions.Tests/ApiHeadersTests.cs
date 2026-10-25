using VolTech.Server.Abstractions;

namespace VolTech.Server.Abstractions.Tests
{
    /// <summary>
    /// ヘッダー名は、クライアントの VolTech.Client.ApiHeaders と同じ文字列でなければならない。
    /// 変えると古いアプリが通信できなくなるので、値を固定して確かめる。
    /// </summary>
    public class ApiHeadersTests
    {
        [Fact]
        public void Names_MatchTheClient()
        {
            Assert.Equal("authorization", ApiHeaders.Authorization);
            Assert.Equal("x-request-id", ApiHeaders.RequestId);
            Assert.Equal("x-app-platform", ApiHeaders.AppPlatform);
            Assert.Equal("x-app-version", ApiHeaders.AppVersion);
            Assert.Equal("x-game-date", ApiHeaders.GameDate);
            Assert.Equal("x-server-time", ApiHeaders.ServerTime);
            Assert.Equal("grpc-status-details-bin", ApiHeaders.ErrorDetailsTrailer);
        }

        [Fact]
        public void MetadataKeys_MatchTheClient()
        {
            Assert.Equal("action", ErrorMetadataKeys.Action);
            Assert.Equal("end_at", ErrorMetadataKeys.EndAt);
        }
    }
}
