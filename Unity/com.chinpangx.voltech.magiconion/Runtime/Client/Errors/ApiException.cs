using System;

namespace VolTech.Client
{
    /// <summary>
    /// API の呼び出しの失敗。共通エラーハンドラーの処理を済ませた後に基盤が投げ、ToResult が受け止める。
    /// </summary>
    /// <remarks>
    /// アプリは try/catch を書かず、ToResult で結果を受け取る。ToResult を付け忘れると、この例外が呼び出し側に届く。
    /// </remarks>
    public sealed class ApiException : Exception
    {
        internal ApiException(ApiError error)
            : base(error.ToString())
        {
            Error = error;
        }

        /// <summary>失敗の内容。</summary>
        public ApiError Error { get; }
    }
}
