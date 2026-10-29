using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// アプリが知っているエラーコードと、その既定の action を渡す。基盤の ErrorPolicyProvider が実装する。
    /// </summary>
    public interface IErrorPolicyProvider
    {
        /// <summary>
        /// 知っているエラーコードかを返す。知らないコードは UNKNOWN_ERROR として扱う。
        /// </summary>
        public bool IsKnown(ErrorCode code);

        /// <summary>
        /// エラーコードの既定の action を返す。知らないコードは Title。
        /// </summary>
        public ErrorAction GetDefaultAction(ErrorCode code);
    }
}
