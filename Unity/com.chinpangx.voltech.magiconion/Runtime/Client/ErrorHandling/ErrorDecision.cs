namespace VolTech.Client
{
    /// <summary>
    /// エラーハンドラーが決めた、エラーの後の動き。
    /// </summary>
    /// <remarks>
    /// 画面の遷移はエラーハンドラーではなく ApiCore が行う(先にセッションを終わらせてから IErrorNavigator を呼ぶ)。
    /// </remarks>
    public enum ErrorDecision
    {
        /// <summary>同じ RequestID で送り直す。</summary>
        Retry,

        /// <summary>諦めて、画面に留まる。</summary>
        Stay,

        /// <summary>諦めて、ホームに戻る。送信中・順番待ちのリクエストは止めて失敗にする。</summary>
        ReturnToHome,

        /// <summary>諦めて、タイトルに戻る。送信中・順番待ちのリクエストを止め、セッションも破棄する。</summary>
        ReturnToTitle,

        /// <summary>諦めて、ストアのページを開く。画面には留まる。</summary>
        OpenStore,
    }
}
