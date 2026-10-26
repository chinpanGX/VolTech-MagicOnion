using System;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using MagicOnion.Client;
using MagicOnion.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;

namespace VolTech.Sandbox.Smoke
{
    [MagicOnionClientGeneration(typeof(ISmokeService))]
    partial class SmokeClientInitializer
    {
    }

    /// <summary>
    /// YetAnotherHttpHandler(h2c)で SmokeService を 1 回呼び、結果を文字列で返す。
    /// IL2CPP と同じ経路を通すため、ソースジェネレーターが作ったクライアントとリゾルバーを使う。
    /// </summary>
    public static class SmokeClient
    {
        public static async Task<string> RunAsync(string address)
        {
            // Play モード外(Editor から直接呼ぶ)では自動登録が走らないので、リゾルバーを明示して渡す
            var options = MessagePackSerializerOptions.Standard
                .WithResolver(CompositeResolver.Create(SmokeClientInitializer.Resolver, StandardResolver.Instance));
            var serializerProvider = MessagePackMagicOnionSerializerProvider.Default.WithOptions(options);

            var handler = new YetAnotherHttpHandler { Http2Only = true };
            using var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = handler,
                DisposeHttpClient = true,
            });

            var client = MagicOnionClient.Create<ISmokeService>(
                channel, serializerProvider, Array.Empty<IClientFilter>(), SmokeClientInitializer.ClientFactoryProvider);
            var headers = new Metadata { { "x-request-id", "smoke-1" } };
            var call = client
                .WithOptions(new CallOptions(headers, DateTime.UtcNow.AddSeconds(5)))
                .EchoAsync("hello", 21);

            var responseHeaders = await call.ResponseHeadersAsync.ConfigureAwait(false);
            var reply = await call.ResponseAsync.ConfigureAwait(false);

            return $"message={reply.Message} number={reply.Number} requestId={reply.RequestId} "
                + $"x-server-time={responseHeaders.GetValue("x-server-time") ?? "(none)"} "
                + $"client={client.GetType().FullName}";
        }
    }
}
