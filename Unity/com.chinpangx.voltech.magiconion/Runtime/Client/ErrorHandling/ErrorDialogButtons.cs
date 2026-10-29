namespace VolTech.Client
{
    /// <summary>
    /// エラーダイアログに出すボタンの組み合わせ。
    /// </summary>
    public enum ErrorDialogButtons
    {
        /// <summary>「OK」だけ。閉じて画面に留まる。</summary>
        Ok,

        /// <summary>「再試行」と「タイトルへ」。</summary>
        RetryOrTitle,

        /// <summary>「ホームへ」だけ。</summary>
        Home,

        /// <summary>「タイトルへ」だけ。</summary>
        Title,

        /// <summary>「ストアへ」だけ。</summary>
        Store,
    }
}
