namespace VolTech.Client.Auth
{
    /// <summary>
    /// 端末に保存したアカウント情報の状態。タイトル画面で、サインアップとサインインのどちらに進むかを決めるのに使う。
    /// </summary>
    public enum AccountStatus
    {
        /// <summary>保存されていない。サインアップ(または引き継ぎ)に進む。</summary>
        NotRegistered,

        /// <summary>保存されている。サインインに進む。</summary>
        Registered,

        /// <summary>
        /// ファイルはあるのに読めない(壊れている・書き換えられている)。問い合わせ・引き継ぎ・新しく始める、を選ばせる。
        /// 新しく始めるときは、IAccountStore.ClearAsync で消してから ApiCore.ResetSession を呼び、サインアップに進む。
        /// </summary>
        Corrupted,
    }
}
