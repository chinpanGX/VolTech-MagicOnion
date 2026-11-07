using MagicOnion.Client;
using MagicOnion.Serialization;
using MagicOnion.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// テストの共有インターフェースのクライアントを、MagicOnion のソースジェネレーターで作る。
    /// </summary>
    [MagicOnionClientGeneration(typeof(IClientBehaviorService))]
    public partial class TestClients
    {
        /// <summary>レスポンスのバイト列を作るのにも使う設定。</summary>
        public static MessagePackSerializerOptions SerializerOptions =>
            MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(Resolver, StandardResolver.Instance));

        public static IMagicOnionSerializerProvider SerializerProvider =>
            MessagePackMagicOnionSerializerProvider.Default.WithOptions(SerializerOptions);
    }
}
