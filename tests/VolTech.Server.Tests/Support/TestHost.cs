using Grpc.Net.Client;
using MagicOnion.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using VolTech.Server.Auth;
using VolTech.Server.Hosting;

namespace VolTech.Server.Tests.Support
{
    /// <summary>
    /// 基盤を登録したサーバーをメモリの中で動かし、MagicOnion のクライアントから呼べるようにする。
    /// </summary>
    public sealed class TestHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private TestHost(WebApplication app, GrpcChannel channel, FakeTimeProvider time, FakePlayerResolver players)
        {
            this.app = app;
            Channel = channel;
            Time = time;
            Players = players;
        }

        public GrpcChannel Channel { get; }

        public FakeTimeProvider Time { get; }

        public FakePlayerResolver Players { get; }

        /// <summary>
        /// サーバーを起動する。connectionString を渡さなければ、DB につながらない(つなごうとすると失敗する)。
        /// </summary>
        public static async Task<TestHost> StartAsync(string? connectionString = null, DateTimeOffset? now = null)
        {
            var time = new FakeTimeProvider(now ?? new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
            var players = new FakePlayerResolver();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<TimeProvider>(time);
            builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString ?? "Host=127.0.0.1;Port=1;Timeout=1"));
            builder.Services.AddSingleton<IPlayerResolver>(players);
            builder.Services.AddVoltechServer(options =>
            {
                options.GameDay.ResetHour = 4;
                options.GameDay.TimeZone = "Asia/Tokyo";
            });

            var app = builder.Build();
            app.MapMagicOnionService();
            await app.StartAsync();

            var handler = new ResponseVersionHandler { InnerHandler = app.GetTestServer().CreateHandler() };
            var channel = GrpcChannel.ForAddress(app.GetTestServer().BaseAddress, new GrpcChannelOptions { HttpHandler = handler });
            return new TestHost(app, channel, time, players);
        }

        public async ValueTask DisposeAsync()
        {
            Channel.Dispose();
            await app.DisposeAsync();
        }

        /// <summary>
        /// TestServer のレスポンスは HTTP/1.1 になるので、gRPC のクライアントが受け付けるよう、リクエストの版に合わせる。
        /// </summary>
        private sealed class ResponseVersionHandler : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = await base.SendAsync(request, cancellationToken);
                response.Version = request.Version;
                return response;
            }
        }
    }

    /// <summary>
    /// プレイヤーの状態を、テストから決められる IPlayerResolver。決めていなければ NotCreated。
    /// </summary>
    public sealed class FakePlayerResolver : IPlayerResolver
    {
        private readonly HashSet<Ulid> created = new();

        public void MarkCreated(Ulid playerId)
        {
            lock (created)
            {
                created.Add(playerId);
            }
        }

        public ValueTask<PlayerStatus> ResolveAsync(Ulid playerId, CancellationToken cancellationToken)
        {
            lock (created)
            {
                return ValueTask.FromResult(created.Contains(playerId) ? PlayerStatus.Active : PlayerStatus.NotCreated);
            }
        }
    }
}
