using MagicOnion.Client;
using MagicOnion.Serialization;
using MagicOnion.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;
using Unity.Scripting.LifecycleManagement;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.E2E
{
    /// <summary>
    /// E2E のテストのクライアントを、MagicOnion のソースジェネレーターで作る。IDeviceService は Abstractions のアセンブリにあるので別に書く。
    /// </summary>
    [MagicOnionClientGeneration(typeof(IE2EService), typeof(IDeviceService))]
    [NoAutoStaticsCleanup]
    public partial class E2EClients
    {
        public static IMagicOnionSerializerProvider SerializerProvider =>
            MessagePackMagicOnionSerializerProvider.Default.WithOptions(
                MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(Resolver, StandardResolver.Instance)));
    }
}
