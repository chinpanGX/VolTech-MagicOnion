namespace VolTech.Client
{
    /// <summary>
    /// エラーが起きたときにクライアントが取る行動。サーバーは ErrorInfo.metadata["action"] で上書きできる(ERROR_ACTION_STAY など)。
    /// </summary>
    public enum ErrorAction
    {
        Unspecified = 0,
        Stay = 1,
        Retry = 2,
        Home = 3,
        Title = 4,
        Update = 5,
    }
}
