using Google.Rpc;
using Grpc.Core;
using MagicOnion.Client;
using VolTech.Server.Abstractions;
using VolTech.Server.Tests.Support;

namespace VolTech.Server.Tests
{
    /// <summary>
    /// デバイス登録・トークンの発行・認証・日付変更。PostgreSQL を使う。
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public class AuthTests
    {
        private const string SecretKey = "0123456789abcdef-secret";

        private readonly PostgresFixture database;

        public AuthTests(PostgresFixture database)
        {
            this.database = database;
        }

        [Fact]
        public async Task RegisteredDevice_CanCallAuthenticatedRpc()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var devices = MagicOnionClient.Create<IDeviceService>(host.Channel);

            var registered = await devices.RegisterAsync(SecretKey);
            var issued = await devices.IssueTokenAsync(registered.DeviceId, SecretKey);
            var players = MagicOnionClient.Create<IPlayerTestService>(host.Channel).WithHeaders(Bearer(issued.AccessToken));

            Assert.Equal(26, registered.DeviceId.Length);
            Assert.Equal(64, issued.AccessToken.Length);
            Assert.Equal(host.Time.GetUtcNow().AddHours(1).ToUnixTimeSeconds(), new DateTimeOffset(issued.ExpiresAt).ToUnixTimeSeconds());
            Assert.Equal($"{registered.PlayerId} created=False", await players.WhoAmIAsync());

            host.Players.MarkCreated(Ulid.Parse(registered.PlayerId));
            Assert.Equal($"{registered.PlayerId} created=True", await players.WhoAmIAsync());
        }

        [Fact]
        public async Task Register_RejectsInvalidSecretKey()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var devices = MagicOnionClient.Create<IDeviceService>(host.Channel);

            var tooShort = await Assert.ThrowsAsync<RpcException>(async () => await devices.RegisterAsync("short"));
            var withSpace = await Assert.ThrowsAsync<RpcException>(async () => await devices.RegisterAsync("0123456789 abcdef"));

            Assert.Equal(StatusCode.InvalidArgument, tooShort.StatusCode);
            Assert.Equal(StatusCode.InvalidArgument, withSpace.StatusCode);
        }

        [Fact]
        public async Task IssueToken_RejectsWrongCredentials()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var devices = MagicOnionClient.Create<IDeviceService>(host.Channel);
            var registered = await devices.RegisterAsync(SecretKey);

            var wrongSecret = await Assert.ThrowsAsync<RpcException>(
                async () => await devices.IssueTokenAsync(registered.DeviceId, SecretKey + "x"));
            var unknownDevice = await Assert.ThrowsAsync<RpcException>(
                async () => await devices.IssueTokenAsync(Ulid.NewUlid().ToString(), SecretKey));

            Assert.Equal(StatusCode.Unauthenticated, wrongSecret.StatusCode);
            Assert.Equal(StatusCode.Unauthenticated, unknownDevice.StatusCode);
        }

        [Fact]
        public async Task ExpiredToken_IsUnauthenticated()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await IssueTokenAsync(host);
            var players = MagicOnionClient.Create<IPlayerTestService>(host.Channel).WithHeaders(Bearer(token));

            host.Time.Advance(TimeSpan.FromHours(1));
            var exception = await Assert.ThrowsAsync<RpcException>(async () => await players.WhoAmIAsync());

            Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
        }

        [Fact]
        public async Task ReissuedToken_InvalidatesTheOldOne()
        {
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var devices = MagicOnionClient.Create<IDeviceService>(host.Channel);
            var registered = await devices.RegisterAsync(SecretKey);
            var first = await devices.IssueTokenAsync(registered.DeviceId, SecretKey);
            var second = await devices.IssueTokenAsync(registered.DeviceId, SecretKey);

            var oldToken = MagicOnionClient.Create<IPlayerTestService>(host.Channel).WithHeaders(Bearer(first.AccessToken));
            var newToken = MagicOnionClient.Create<IPlayerTestService>(host.Channel).WithHeaders(Bearer(second.AccessToken));

            var exception = await Assert.ThrowsAsync<RpcException>(async () => await oldToken.WhoAmIAsync());
            Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
            Assert.StartsWith(registered.PlayerId, await newToken.WhoAmIAsync());
        }

        [Fact]
        public async Task OldGameDate_IsDateChanged()
        {
            // 2026-10-06 12:00 UTC は、JST で 21:00 なので 10-06
            await using var host = await TestHost.StartAsync(database.ConnectionString);
            var token = await IssueTokenAsync(host);
            var players = MagicOnionClient.Create<IPlayerTestService>(host.Channel);

            var today = players.WithHeaders(Bearer(token, gameDate: "2026-10-06"));
            var yesterday = players.WithHeaders(Bearer(token, gameDate: "2026-10-05"));
            var withoutDate = players.WithHeaders(Bearer(token));

            await today.WhoAmIAsync();
            await withoutDate.WhoAmIAsync();
            var exception = await Assert.ThrowsAsync<RpcException>(async () => await yesterday.WhoAmIAsync());
            Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
            var info = exception.GetRpcStatus()?.GetDetail<ErrorInfo>();
            Assert.NotNull(info);
            Assert.Equal(ReservedErrors.Domain, info.Domain);
            Assert.Equal("DATE_CHANGED", info.Reason);
        }

        internal static async Task<string> IssueTokenAsync(TestHost host)
        {
            var devices = MagicOnionClient.Create<IDeviceService>(host.Channel);
            var registered = await devices.RegisterAsync(SecretKey);
            var issued = await devices.IssueTokenAsync(registered.DeviceId, SecretKey);
            return issued.AccessToken;
        }

        internal static Metadata Bearer(string token, string? requestId = null, string? gameDate = null)
        {
            var headers = new Metadata { { ApiHeaders.Authorization, "Bearer " + token } };
            if (requestId != null)
            {
                headers.Add(ApiHeaders.RequestId, requestId);
            }

            if (gameDate != null)
            {
                headers.Add(ApiHeaders.GameDate, gameDate);
            }

            return headers;
        }
    }
}
