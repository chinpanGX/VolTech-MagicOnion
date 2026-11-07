namespace VolTech.Server.Internal
{
    /// <summary>
    /// フィルターがリクエストごとの値を ServiceContext.Items に入れるときのキー。
    /// </summary>
    internal static class ContextItems
    {
        public const string GameDate = "voltech.game-date";
        public const string AuthContext = "voltech.auth";
        public const string RpcLog = "voltech.rpc-log";

        /// <summary>ITransactions.RunAsync が返したレスポンス。CommonResponseCheckFilter が読む。</summary>
        public const string RunResponse = "voltech.run-response";
    }
}
