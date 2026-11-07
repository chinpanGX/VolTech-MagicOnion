using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using VolTech.E2E.Server;
using VolTech.Server.Auth;
using VolTech.Server.Hosting;
using VolTech.Server.Transaction;

// Unity の PlayMode テストの相手のサーバー。本物の基盤を h2c で起動する。Docker が要る
var port = args.Length > 0 ? int.Parse(args[0]) : 5199;

await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
await database.StartAsync();
var connectionString = database.GetConnectionString();
VoltechDatabase.Migrate(connectionString);

var builder = WebApplication.CreateBuilder();
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port, listen => listen.Protocols = HttpProtocols.Http2));
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<IPlayerResolver, ActivePlayerResolver>();
builder.Services.AddSingleton<E2EState>();
builder.Services
    .AddVoltechServer(options =>
    {
        options.GameDay.ResetHour = 4;
        options.GameDay.TimeZone = "Asia/Tokyo";
    })
    .AddCommonResponse<VolTech.Sandbox.E2E.E2ECommon, E2ECollector>();

var app = builder.Build();
app.MapMagicOnionService();
Console.WriteLine($"VolTech.E2E.Server listening on http://localhost:{port}");
await app.RunAsync();
