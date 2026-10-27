namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// 基盤の予約エラー(domain "voltech")。アプリの業務エラーは、アプリの domain で定義する。
    /// </summary>
    /// <remarks>
    /// reason を消しても使い回さない。古いアプリは知らないコードを UNKNOWN_ERROR として扱う。
    /// </remarks>
    public static class ReservedErrors
    {
        /// <summary>予約エラーの domain(ErrorInfo.domain)。</summary>
        public const string Domain = "voltech";

        /// <summary>サーバー内部エラー。一時的な障害は再試行で直り得る。</summary>
        public static readonly ErrorCode InternalError = new ErrorCode(Domain, "INTERNAL_ERROR");

        /// <summary>日付変更。サインインし直し、所持データを全件取り直す。</summary>
        public static readonly ErrorCode DateChanged = new ErrorCode(Domain, "DATE_CHANGED");

        /// <summary>メンテナンス中。終了予定を metadata の end_at に入れる。今のサーバーは返さない。</summary>
        public static readonly ErrorCode Maintenance = new ErrorCode(Domain, "MAINTENANCE");

        /// <summary>強制アップデート。今のサーバーは返さない。</summary>
        public static readonly ErrorCode UpdateRequired = new ErrorCode(Domain, "UPDATE_REQUIRED");

        /// <summary>BAN・アカウント停止。今のサーバーは返さない。</summary>
        public static readonly ErrorCode AccountSuspended = new ErrorCode(Domain, "ACCOUNT_SUSPENDED");

        /// <summary>通信断・タイムアウト。クライアントだけが使う。</summary>
        public static readonly ErrorCode NetworkError = new ErrorCode(Domain, "NETWORK_ERROR");

        /// <summary>想定外のステータス、知らないエラー。クライアントだけが使う。</summary>
        public static readonly ErrorCode UnknownError = new ErrorCode(Domain, "UNKNOWN_ERROR");

        /// <summary>クライアントのバグ(共通レスポンス項目の反映処理での例外など)。クライアントだけが使う。</summary>
        public static readonly ErrorCode ClientError = new ErrorCode(Domain, "CLIENT_ERROR");

        /// <summary>端末に保存したアカウント情報が読めない。クライアントだけが使う。</summary>
        public static readonly ErrorCode AccountDataCorrupted = new ErrorCode(Domain, "ACCOUNT_DATA_CORRUPTED");

        /// <summary>デバイス登録はできたが、アカウント情報を端末に保存できなかった。クライアントだけが使う。</summary>
        public static readonly ErrorCode AccountSaveFailed = new ErrorCode(Domain, "ACCOUNT_SAVE_FAILED");
    }
}
