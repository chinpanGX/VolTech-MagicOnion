using System;
using MagicOnion.Client;
using VolTech.Client;
using VolTech.Client.Auth;
using VolTech.Client.Unity;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// ApiCore を組み立てる。組み立ての間違いをテストで見つけられるように、画面とテストの両方がここを通る。
    /// </summary>
    public static class SampleSetup
    {
        private const int AppVersionNumber = 1;

        /// <summary>
        /// 通信とプレイヤーの状態を作る。
        /// </summary>
        /// <param name="address">サーバーの接続先。</param>
        /// <param name="view">エラーの見た目。</param>
        /// <param name="navigator">エラーのときの画面の遷移。</param>
        public static SampleSession Create(string address, IErrorDialogView view, IErrorNavigator navigator)
        {
            var channel = ApiChannel.Create(address);
            var callInvoker = ApiChannel.CreateCallInvoker(channel);
            var options = new ApiCoreOptions(new AppVersion(AppVersion.Windows, AppVersionNumber));
            var deviceClient = MagicOnionClient.Create<IDeviceService>(
                callInvoker,
                SampleClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                SampleClients.ClientFactoryProvider);

            // DB はサーバーを止めると消えるので、端末情報もメモリーにだけ持つ。ファイルに残すと再起動の後に認証が失敗し続ける
            var tokenProvider = new DeviceTokenProvider(deviceClient, new InMemoryAccountStore(), options);
            var player = new SamplePlayer();
            var core = ApiCore.CreateBuilder(
                    callInvoker,
                    SampleClients.SerializerProvider,
                    SampleClients.ClientFactoryProvider,
                    options,
                    tokenProvider,
                    new ErrorPolicyProvider(new[]
                    {
                        new ErrorDefinition(SampleErrors.NotEnoughGems, ErrorAction.Stay),
                        new ErrorDefinition(SampleErrors.FreeDrawUsed, ErrorAction.Stay),
                    }),
                    new DefaultCommonErrorHandler(view))
                .WithCommonResponseApplier(new SampleCommonApplier(player))
                .WithErrorNavigator(navigator)
                .Build();
            return new SampleSession(channel, core, player);
        }
    }
}
