using System;
using System.Collections.Generic;
using Google.Protobuf;
using Grpc.Core;
using VolTech.Client.Auth;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 受け取ったエラーを、エラーコードと action に変換する。
    /// </summary>
    internal static class ErrorConverter
    {
        /// <summary>
        /// gRPC のエラーをエラーコードに変える。
        /// </summary>
        public static ApiError FromRpcException(RpcException exception, IErrorPolicyProvider policy)
        {
            var message = exception.Status.Detail;
            switch (exception.StatusCode)
            {
                case StatusCode.FailedPrecondition:
                    var errorInfo = TryGetErrorInfo(exception);
                    // 知らないコード(このアプリより新しいサーバーが足したものなど)は UNKNOWN_ERROR と同じ扱いにする
                    var code = errorInfo == null ? default : new ErrorCode(errorInfo.Domain, errorInfo.Reason);
                    if (errorInfo != null && policy.IsKnown(code))
                    {
                        errorInfo.Metadata.TryGetValue(ErrorMetadataKeys.Action, out var actionOverride);
                        return Create(code, exception.StatusCode, message, policy, actionOverride, errorInfo.Metadata);
                    }

                    return Create(ReservedErrors.UnknownError, exception.StatusCode, message, policy);
                case StatusCode.Internal:
                    return Create(ReservedErrors.InternalError, exception.StatusCode, message, policy);
                case StatusCode.Unavailable:
                case StatusCode.DeadlineExceeded:
                    return Create(ReservedErrors.NetworkError, exception.StatusCode, message, policy);
                default:
                    return Create(ReservedErrors.UnknownError, exception.StatusCode, message, policy);
            }
        }

        /// <summary>
        /// クライアント内部の例外を CLIENT_ERROR に変える。アカウント情報の読み書きの失敗は、それぞれ専用のコードにする。
        /// </summary>
        public static ApiError FromClientException(Exception exception, IErrorPolicyProvider policy)
        {
            return Create(ClientErrorCode(exception), StatusCode.Unknown, exception.Message, policy);
        }

        /// <summary>
        /// 基盤の予約エラー(domain voltech)かを返す。予約エラーは handledCodes に渡しても、必ず共通エラーハンドラーが処理する。
        /// </summary>
        public static bool IsReserved(ErrorCode code)
        {
            return code.Domain == ReservedErrors.Domain;
        }

        /// <summary>
        /// 端末のアカウント情報の読み書きの失敗か。通信のエラーではないので、共通エラーハンドラーに回さずに呼び出し側に返す。
        /// </summary>
        public static bool IsAccountStorageError(ErrorCode code)
        {
            return code == ReservedErrors.AccountDataCorrupted || code == ReservedErrors.AccountSaveFailed;
        }

        // バグとは違い、アプリが専用の画面(問い合わせ・引き継ぎなど)に進められるように、別のコードにする
        private static ErrorCode ClientErrorCode(Exception exception)
        {
            switch (exception)
            {
                case CorruptedAccountDataException:
                    return ReservedErrors.AccountDataCorrupted;
                case AccountSaveFailedException:
                    return ReservedErrors.AccountSaveFailed;
                default:
                    return ReservedErrors.ClientError;
            }
        }

        // エラーの詳細が壊れていたら(途中の機器が書き換えた、など)、詳細が無いものとして UNKNOWN_ERROR にする
        private static RpcErrorInfo TryGetErrorInfo(RpcException exception)
        {
            try
            {
                return RpcErrorDetails.GetErrorInfo(exception);
            }
            catch (InvalidProtocolBufferException)
            {
                return null;
            }
        }

        private static ApiError Create(
            ErrorCode code,
            StatusCode statusCode,
            string message,
            IErrorPolicyProvider policy,
            string actionOverride = null,
            IReadOnlyDictionary<string, string> metadata = null)
        {
            var action = ParseAction(actionOverride);
            if (action == ErrorAction.Unspecified)
            {
                action = policy.GetDefaultAction(code);
            }

            if (action == ErrorAction.Unspecified)
            {
                action = ErrorAction.Title;
            }

            return new ApiError(code, action, statusCode, message, metadata);
        }

        // サーバーは metadata["action"] に ErrorAction の値の名前を入れる。知らない値なら既定の action を使う
        private static ErrorAction ParseAction(string name)
        {
            switch (name)
            {
                case "ERROR_ACTION_STAY":
                    return ErrorAction.Stay;
                case "ERROR_ACTION_RETRY":
                    return ErrorAction.Retry;
                case "ERROR_ACTION_HOME":
                    return ErrorAction.Home;
                case "ERROR_ACTION_TITLE":
                    return ErrorAction.Title;
                case "ERROR_ACTION_UPDATE":
                    return ErrorAction.Update;
                default:
                    return ErrorAction.Unspecified;
            }
        }
    }
}
