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
    }
}
