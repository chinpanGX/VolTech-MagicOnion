using System;
using Cysharp.Net.Http;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Unity.Scripting.LifecycleManagement;

namespace VolTech.Client.Unity
{
    /// <summary>
    /// YetAnotherHttpHandler(HTTP/2)で通信する GrpcChannel を作る。チャンネルは1つを使い回す。
    /// </summary>
    /// <remarks>
    /// ApiCore.CreateBuilder と、IDeviceService の MagicOnionClient.Create には、CreateCallInvoker で作った CallInvoker を渡す。
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class ApiChannel
    {
        // つながらないときに早めに諦め、裏で接続を試み続けるのを止める。YetAnotherHttpHandler は既定では無効
        private const int DefaultConnectTimeoutSeconds = 10;

        // 開発用のツール(Monitor)が付ける Interceptor。プレイヤーには付ける側が入らないので、null のまま
        private static Interceptor interceptor;

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
        /// チャンネルで呼ぶ CallInvoker を作る。ApiCore.CreateBuilder に渡す。
        /// </summary>
        public static CallInvoker CreateCallInvoker(GrpcChannel channel)
        {
            var invoker = channel.CreateCallInvoker();
            return interceptor == null ? invoker : invoker.Intercept(interceptor);
        }

        /// <summary>
        /// この後に CreateCallInvoker で作る CallInvoker に付ける Interceptor を決める。Monitor が Editor の起動時に呼ぶ。
        /// </summary>
        internal static void SetInterceptor(Interceptor value)
        {
            interceptor = value;
        }
    }
}
