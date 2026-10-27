using System.Diagnostics;
using Grpc.Core;
using MagicOnion;
using MagicOnion.Server;
using MagicOnion.Server.Filters;
using Microsoft.Extensions.Logging;
using VolTech.Server.Abstractions;
using VolTech.Server.Internal;

namespace VolTech.Server.Logging
{
    /// <summary>
    /// 最も外側のフィルター。RPC ごとのログのスコープを作り、RPC の終わりにアクセスログを 1 行出す。
    /// 例外はここで一元変換する(業務エラーは FAILED_PRECONDITION + ErrorInfo、想定外の例外は INTERNAL)。
    /// </summary>
    internal sealed class RpcLogFilter : IMagicOnionServiceFilter
    {
        private readonly ILogger<RpcLogFilter> logger;

        public RpcLogFilter(ILogger<RpcLogFilter> logger)
        {
            this.logger = logger;
        }

        public async ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
        {
            var settings = RpcSettings.Get(context);
            var scope = new RpcLogScope(settings.FullName, ReadRequestId(context.CallContext));
            context.Items[ContextItems.RpcLog] = scope;

            using var loggerScope = logger.BeginScope(scope);
            var started = Stopwatch.GetTimestamp();
            var statusCode = StatusCode.OK;
            ErrorCode? errorCode = null;
            try
            {
                await next(context);
            }
            catch (BusinessException exception)
            {
                statusCode = StatusCode.FailedPrecondition;
                errorCode = exception.Code;
                throw ErrorStatus.FromBusinessException(exception);
            }
            catch (ReturnStatusException exception)
            {
                // AuthFilter の UNAUTHENTICATED など。INTERNAL に変えるとクライアントの再認証が動かないので、そのまま通す
                statusCode = exception.StatusCode;
                throw;
            }
            catch (RpcException exception)
            {
                statusCode = exception.StatusCode;
                throw;
            }
            catch (OperationCanceledException) when (context.CallContext.CancellationToken.IsCancellationRequested)
            {
                // クライアントの切断・締め切り切れ。サーバーの不具合ではないので error にしない
                statusCode = StatusCode.Cancelled;
                throw new RpcException(new Grpc.Core.Status(StatusCode.Cancelled, "cancelled"));
            }
            catch (Exception exception)
            {
                statusCode = StatusCode.Internal;
                errorCode = ReservedErrors.InternalError;
                logger.LogError(exception, "internal error");
                throw ErrorStatus.Internal();
            }
            finally
            {
                LogAccess(statusCode, errorCode, Stopwatch.GetElapsedTime(started));
            }
        }

        private void LogAccess(StatusCode statusCode, ErrorCode? errorCode, TimeSpan elapsed)
        {
            // 業務エラー(FAILED_PRECONDITION など)は error にしない。サーバーの不具合と見分けるため
            var level = statusCode is StatusCode.Unknown or StatusCode.Internal or StatusCode.DataLoss
                ? LogLevel.Error
                : LogLevel.Information;
            logger.Log(
                level,
                "rpc finished {rpc.response.status_code} {error.domain} {error.reason} {duration_ms}",
                ToCodeName(statusCode),
                errorCode?.Domain,
                errorCode?.Reason,
                elapsed.TotalMilliseconds);
        }

        private static string? ReadRequestId(ServerCallContext callContext)
        {
            var value = callContext.RequestHeaders.GetValue(ApiHeaders.RequestId);
            return RequestIdRules.IsValid(value) ? value : null;
        }

        /// <summary>gRPC のステータスの名前(google.rpc.Code と同じ書き方)。</summary>
        internal static string ToCodeName(StatusCode statusCode)
        {
            return statusCode switch
            {
                StatusCode.OK => "OK",
                StatusCode.Cancelled => "CANCELLED",
                StatusCode.Unknown => "UNKNOWN",
                StatusCode.InvalidArgument => "INVALID_ARGUMENT",
                StatusCode.DeadlineExceeded => "DEADLINE_EXCEEDED",
                StatusCode.NotFound => "NOT_FOUND",
                StatusCode.AlreadyExists => "ALREADY_EXISTS",
                StatusCode.PermissionDenied => "PERMISSION_DENIED",
                StatusCode.ResourceExhausted => "RESOURCE_EXHAUSTED",
                StatusCode.FailedPrecondition => "FAILED_PRECONDITION",
                StatusCode.Aborted => "ABORTED",
                StatusCode.OutOfRange => "OUT_OF_RANGE",
                StatusCode.Unimplemented => "UNIMPLEMENTED",
                StatusCode.Internal => "INTERNAL",
                StatusCode.Unavailable => "UNAVAILABLE",
                StatusCode.DataLoss => "DATA_LOSS",
                StatusCode.Unauthenticated => "UNAUTHENTICATED",
                _ => statusCode.ToString(),
            };
        }
    }
}
