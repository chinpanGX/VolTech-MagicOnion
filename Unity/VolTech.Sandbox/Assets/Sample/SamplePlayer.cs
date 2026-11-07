namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// 画面に出すプレイヤーの状態。Sandbox はドメインのリロードを切っていて、static は Play をまたいで残るので、SampleSession が持つ。
    /// </summary>
    public sealed class SamplePlayer
    {
        /// <summary>残りのジェム。</summary>
        public int Gems { get; set; }

        /// <summary>今日の無料ガチャを引けるか。</summary>
        public bool FreeDrawAvailable { get; set; }

        /// <summary>
        /// ホームの全件で置き換える。
        /// </summary>
        public void ReplaceAll(HomeResponse home)
        {
            Gems = home.Gems;
            FreeDrawAvailable = home.FreeDrawAvailable;
        }
    }
}
