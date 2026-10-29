using System;

namespace VolTech.Client
{
    /// <summary>
    /// 通信のログを出す。Unity 用の層が Debug.Log につなぐ。
    /// </summary>
    /// <remarks>
    /// 開発中の確認用。リリースのビルドでは ApiCore に渡さない(null にする)。渡さなければ、ログの文字列も作らない。
    /// </remarks>
    public interface IApiLogger
    {
        /// <summary>
        /// 通常のログを出す。
        /// </summary>
        public void Log(string message);

        /// <summary>
        /// 例外をスタックトレース付きでログに出す。
        /// </summary>
        public void LogException(string message, Exception exception);
    }
}
