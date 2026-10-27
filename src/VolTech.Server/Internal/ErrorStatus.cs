using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using VolTech.Server.Abstractions;

namespace VolTech.Server.Internal
{
    /// <summary>
    /// クライアントへ返すエラー(google.rpc.Status を grpc-status-details-bin に入れた RpcException)を作る。
    /// </summary>
    internal static class ErrorStatus
    {
        /// <summary>業務エラーを FAILED_PRECONDITION + ErrorInfo にする。</summary>
        public static RpcException FromBusinessException(BusinessException exception)
        {
            return Create(StatusCode.FailedPrecondition, exception.Code, exception.Message, exception.Metadata);
        }

        /// <summary>サーバー内部エラー。詳細はクライアントに返さない(ログにだけ出す)。</summary>
        public static RpcException Internal()
        {
            return Create(StatusCode.Internal, ReservedErrors.InternalError, "internal error", null);
        }

        private static RpcException Create(
            StatusCode statusCode,
            ErrorCode code,
            string message,
            IReadOnlyDictionary<string, string>? metadata)
        {
            var info = new ErrorInfo
            {
                Domain = code.Domain,
                Reason = code.Reason,
            };
            if (metadata != null)
            {
                foreach (var pair in metadata)
                {
                    info.Metadata[pair.Key] = pair.Value;
                }
            }

            var status = new Google.Rpc.Status
            {
                Code = (int)statusCode,
                Message = message,
                Details = { Any.Pack(info) },
            };
            return status.ToRpcException();
        }
    }
}
