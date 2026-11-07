using System;
using Grpc.Core;
using MagicOnion.Client;
using MagicOnion.Serialization;
using MagicOnion.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;
using Unity.Scripting.LifecycleManagement;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// サンプルのクライアントを、MagicOnion のソースジェネレーターで作る。IDeviceService は Abstractions のアセンブリにあるので別に書く。
    /// </summary>
    [MagicOnionClientGeneration(typeof(ISampleGachaService), typeof(IDeviceService))]
    [NoAutoStaticsCleanup]
    public partial class SampleClients
    {
        /// <summary>サンプルサーバーの接続先。サーバーのポートも固定。</summary>
        public const string Address = "http://localhost:5200";

        public static IMagicOnionSerializerProvider SerializerProvider =>
            MessagePackMagicOnionSerializerProvider.Default.WithOptions(
                MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(Resolver, StandardResolver.Instance)));

        /// <summary>
        /// 時刻の RPC のクライアントを作る。トークンが要らないので ApiCore を通さない。
        /// </summary>
        public static IServerClockService CreateClockClient(CallInvoker callInvoker)
        {
            return MagicOnionClient.Create<IServerClockService>(
                callInvoker,
                SerializerProvider,
                Array.Empty<IClientFilter>(),
                ClientFactoryProvider);
        }
    }
}
