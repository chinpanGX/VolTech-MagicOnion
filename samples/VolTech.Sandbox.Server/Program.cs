using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using VolTech.Sandbox.Sample;
using VolTech.Sandbox.Server;
using VolTech.Server.Auth;
using VolTech.Server.Hosting;
using VolTech.Server.Transaction;

// Sandbox のサンプルの相手のサーバー。Docker が要る。データと時刻のずれは止めると消える
const int port = 5200;

await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
await database.StartAsync();
var connectionString = database.GetConnectionString();
VoltechDatabase.Migrate(connectionString);

var dataSource = NpgsqlDataSource.Create(connectionString);
await using (var command = dataSource.CreateCommand(SampleGacha.CreateTableSql))
{
    await command.ExecuteNonQueryAsync();
}

var builder = WebApplication.CreateBuilder();
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port, listen => listen.Protocols = HttpProtocols.Http2));
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<IPlayerResolver, ActivePlayerResolver>();

// AddVoltechServer の TryAddSingleton より先に登録して、基盤の時計を差し替える
var timeProvider = new ShiftableTimeProvider();
builder.Services.AddSingleton(timeProvider);
builder.Services.AddSingleton<TimeProvider>(timeProvider);

builder.Services
    .AddVoltechServer(options =>
    {
        options.GameDay.ResetHour = 4;
        options.GameDay.TimeZone = "Asia/Tokyo";
    })
    .AddCommonResponse<SampleCommon, SampleCollector>();

var app = builder.Build();
app.MapMagicOnionService();
Console.WriteLine($"VolTech.Sandbox.Server listening on http://localhost:{port}");
await app.RunAsync();
