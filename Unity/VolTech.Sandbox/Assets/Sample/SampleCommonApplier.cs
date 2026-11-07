using Cysharp.Threading.Tasks;
using VolTech.Client;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// 共通レスポンス項目をプレイヤーの状態に反映する。
    /// </summary>
    public sealed class SampleCommonApplier : ICommonResponseApplier<SampleCommon>
    {
        private readonly SamplePlayer player;

        public SampleCommonApplier(SamplePlayer player)
        {
            this.player = player;
        }

        public UniTask ApplyAsync(SampleCommon common)
        {
            player.Gems = common.Gems;
            return UniTask.CompletedTask;
        }
    }
}
