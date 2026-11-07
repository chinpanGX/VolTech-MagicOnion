using VolTech.Sandbox.Sample;
using VolTech.Server.Transaction;

namespace VolTech.Sandbox.Server
{
    /// <summary>
    /// 共通レスポンス項目を集めるコレクター。ジェムが変わらなければ Common は null になる。
    /// </summary>
    public sealed class SampleCollector : ICommonResponseCollector<SampleCommon>
    {
        /// <summary>変更後のジェム。</summary>
        public int? Gems { get; set; }

        public ValueTask<SampleCommon?> BuildAsync(TxContext tx)
        {
            return ValueTask.FromResult(Gems == null ? null : new SampleCommon { Gems = Gems.Value });
        }
    }
}
