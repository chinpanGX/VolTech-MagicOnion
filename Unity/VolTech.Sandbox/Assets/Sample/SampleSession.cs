using System;
using Cysharp.Threading.Tasks;
using Grpc.Net.Client;
using VolTech.Client;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// サンプルの通信とプレイヤーの状態をまとめる。画面とテストの両方が使う。
    /// </summary>
    public sealed class SampleSession : IDisposable
    {
        private readonly GrpcChannel channel;
        private readonly ISampleGachaService gacha;
        private readonly ISampleGachaService handledGacha;

        internal SampleSession(GrpcChannel channel, ApiCore core, SamplePlayer player)
        {
            this.channel = channel;
            Player = player;
            gacha = core.CreateClient<ISampleGachaService>();
            handledGacha = gacha.WithHandledCodes(SampleErrors.NotEnoughGems, SampleErrors.FreeDrawUsed);
        }

        public SamplePlayer Player { get; }

        /// <summary>
        /// ホームの全件を取り、プレイヤーの状態を置き換える。
        /// </summary>
        public async UniTask<ApiResult<HomeResponse>> EnterAsync()
        {
            var result = await gacha.GetHomeAsync().ToResult();
            if (!result.IsError)
            {
                Player.ReplaceAll(result.Value);
            }

            return result;
        }

        /// <summary>
        /// ガチャを 1 回引く。ジェムは Common で届き、SampleCommonApplier が反映する。
        /// </summary>
        public async UniTask<ApiResult<DrawResponse>> DrawAsync(bool useFree)
        {
            var result = await handledGacha.DrawAsync(new DrawRequest { UseFree = useFree }).ToResult();
            if (!result.IsError)
            {
                Player.FreeDrawAvailable = result.Value.FreeDrawAvailable;
            }

            return result;
        }

        public void Dispose()
        {
            channel.Dispose();
        }
    }
}
