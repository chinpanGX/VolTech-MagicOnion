using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using MagicOnion;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 偽のサーバー(テストで共有インターフェースを実装したもの)が投げる、サーバーと同じ形のエラーを作る。
    /// </summary>
    public static class FakeRpcErrors
    {
        /// <summary>
        /// 業務エラー(FAILED_PRECONDITION + ErrorInfo)を作る。code の domain と reason が ErrorInfo に入る。
        /// </summary>
        /// <param name="metadata">ErrorInfo.metadata に入れる値(例: MAINTENANCE の end_at)。</param>
        public static RpcException FailedPrecondition(
            ErrorCode code,
            string message = "",
            IReadOnlyDictionary<string, string> metadata = null)
        {
            var errorInfo = new RpcErrorInfo(code.Reason, code.Domain, metadata);
            var status = RpcErrorDetails.EncodeStatus((int)StatusCode.FailedPrecondition, message, errorInfo);
            var trailers = new Metadata { { RpcErrorDetails.TrailerKey, status } };
            return new RpcException(new global::Grpc.Core.Status(StatusCode.FailedPrecondition, message), trailers);
        }

        /// <summary>
        /// 失敗した呼び出しを作る。アプリの偽のクライアントが返すと、ToResult が失敗の結果にする。
        /// </summary>
        /// <remarks>
        /// 偽のクライアントのメソッドから同期で例外を投げると、ToResult に届かない。
        /// </remarks>
        /// <param name="code">エラーコード。</param>
        /// <param name="message">ログ用のメッセージ。</param>
        /// <param name="action">エラーの後に取る行動。</param>
        /// <typeparam name="T">レスポンスの型。</typeparam>
        public static UnaryResult<T> ApiFailureResult<T>(
            ErrorCode code,
            string message = "",
            ErrorAction action = ErrorAction.Unspecified)
        {
            var error = new ApiError(code, action, StatusCode.FailedPrecondition, message);
            return new UnaryResult<T>(Task.FromException<T>(new ApiException(error)));
        }

        /// <summary>
        /// 詳細の無いエラー(UNAUTHENTICATED・UNAVAILABLE など)を作る。
        /// </summary>
        public static RpcException Status(StatusCode statusCode, string message = "")
        {
            return new RpcException(new global::Grpc.Core.Status(statusCode, message));
        }
    }
}
