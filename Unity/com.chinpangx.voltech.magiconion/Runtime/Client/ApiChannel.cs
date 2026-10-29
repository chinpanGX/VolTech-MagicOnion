using System;
using Cysharp.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;

namespace VolTech.Client.Unity
{
    /// <summary>
    /// YetAnotherHttpHandler(HTTP/2)で通信する GrpcChannel を作る。チャンネルは1つを使い回す。
    /// </summary>
    /// <remarks>
    /// MagicOnionClient.Create にはチャンネルか、CreateCallInvoker で作った CallInvoker を渡す。
    /// </remarks>
    public static class ApiChannel
    {
        // つながらないときに早めに諦め、裏で接続を試み続けるのを止める。YetAnotherHttpHandler は既定では無効
        private const int DefaultConnectTimeoutSeconds = 10;

        /// <summary>
        /// 接続先の URL(例: http://localhost:50051)へのチャンネルを作る。
        /// </summary>
        public static GrpcChannel Create(string address, TimeSpan? connectTimeout = null)
        {
            var handler = new YetAnotherHttpHandler
            {
                // ローカルのサーバーは TLS なしの HTTP/2(h2c)で待ち受ける。指定しないと HTTP/1 になる
                Http2Only = address.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(DefaultConnectTimeoutSeconds),
            };
            return GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = handler,
                DisposeHttpClient = true,
            });
        }

        /// <summary>
        /// チャンネルで呼ぶ CallInvoker を作る。MagicOnionClient.Create に渡す。
        /// </summary>
        /// <remarks>
        /// VolTech-gRPC 版は、開発用のツール(gRPC Monitor)が足した Interceptor をここで付けていた。
        /// Tracing は移していない(docs/design/client.md)ので、チャンネルの CallInvoker をそのまま返す。
        /// </remarks>
        public static CallInvoker CreateCallInvoker(GrpcChannel channel)
        {
            return channel.CreateCallInvoker();
        }
    }
}
