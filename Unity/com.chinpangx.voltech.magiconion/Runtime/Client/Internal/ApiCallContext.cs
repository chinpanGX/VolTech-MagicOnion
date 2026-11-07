using System;
using System.Threading;

namespace VolTech.Client
{
    /// <summary>
    /// 1 回の試行の値。ApiCore が作り、ApiCallInvoker が読む。
    /// </summary>
    internal sealed class ApiCallContext
    {
        public ApiCallContext(string token, string requestId, string gameDate, CancellationToken cancellationToken)
        {
            Token = token;
            RequestId = requestId;
            GameDate = gameDate;
            CancellationToken = cancellationToken;
        }

        /// <summary>アクセストークン。[SkipAuth] の RPC では null。</summary>
        public string Token { get; }

        /// <summary>x-request-id の値。冪等性の対象でなければ null。</summary>
        public string RequestId { get; }

        /// <summary>x-game-date の値。まだ持っていなければ null。</summary>
        public string GameDate { get; }

        /// <summary>呼び出しの開始時に読んだセッションのキャンセル。再送でも同じものを使う。</summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// 応答が届いた後にレスポンスを読む処理。フィルターはレスポンスの型を知らないので、型を知っている ApiCallInvoker が入れる。
        /// </summary>
        public Func<object> ReadResponse { get; set; }
    }
}
