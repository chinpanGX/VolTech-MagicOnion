namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// ErrorInfo.metadata で基盤が意味を持たせているキー。
    /// </summary>
    public static class ErrorMetadataKeys
    {
        /// <summary>
        /// クライアントの既定の action を上書きする。値は ERROR_ACTION_STAY・ERROR_ACTION_RETRY・ERROR_ACTION_HOME・
        /// ERROR_ACTION_TITLE・ERROR_ACTION_UPDATE のどれか。アプリの metadata には使えない。
        /// </summary>
        public const string Action = "action";

        /// <summary>
        /// MAINTENANCE の終了予定時刻。UTC の RFC 3339。
        /// </summary>
        public const string EndAt = "end_at";
    }
}
