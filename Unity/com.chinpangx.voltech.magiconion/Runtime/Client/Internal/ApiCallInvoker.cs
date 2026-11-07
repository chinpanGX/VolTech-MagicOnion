using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using MagicOnion.Client;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 試行ごとのヘッダー・締め切り・キャンセルを付けて通信する CallInvoker。
    /// </summary>
    /// <remarks>
    /// フィルターでは付けられないものをここで付ける。CallOptions は読み取り専用で、締め切りとキャンセルを変えられない。
    /// Metadata はクライアントごとに使い回されるので、書き換えると同時の呼び出しで混ざる。
    /// ヘッダーは試行ごとに新しく作り、CallOptions.Headers の中身(x-voltech-call を含む)は送らない。
    /// 試行の値は AsyncLocal で受け取る。next を呼ぶと、その呼び出しの中で AsyncUnaryCall まで進むので、await をまたいだ引き継ぎには頼らない。
    /// </remarks>
    internal sealed class ApiCallInvoker : CallInvoker
    {
        private readonly CallInvoker inner;
        private readonly ApiCoreOptions options;
        private readonly AsyncLocal<ApiCallContext> current = new();

        public ApiCallInvoker(CallInvoker inner, ApiCoreOptions options)
        {
            this.inner = inner;
            this.options = options;
        }

        /// <summary>
        /// callContext の値で 1 回送る。ValueTask を受け取った時点(await の前)で、通信は始まっていて、値は外れている。
        /// </summary>
        public ValueTask<ResponseContext> Send(
            ApiCallContext callContext,
            RequestContext context,
            Func<RequestContext, ValueTask<ResponseContext>> next)
        {
            current.Value = callContext;
            try
            {
                return next(context);
            }
            finally
            {
                current.Value = null;
            }
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions callOptions,
            TRequest request)
        {
            var callContext = current.Value ?? throw new InvalidOperationException("ApiCallContext が無い。ApiCore を通さずに呼んだか、next から CallInvoker まで同じ流れで進まなかった。");
            var call = inner.AsyncUnaryCall(
                method,
                host,
                callOptions
                    .WithHeaders(BuildHeaders(callContext))
                    .WithDeadline(DateTime.UtcNow.Add(options.Deadline))
                    .WithCancellationToken(callContext.CancellationToken),
                request);
            callContext.ReadResponse = () => call.ResponseAsync.Result;
            return call;
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions callOptions,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions callOptions,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions callOptions)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions callOptions)
        {
            throw new NotSupportedException();
        }

        private Metadata BuildHeaders(ApiCallContext callContext)
        {
            var headers = new Metadata();
            if (callContext.Token != null)
            {
                headers.Add(ApiHeaders.Authorization, $"Bearer {callContext.Token}");
            }

            if (callContext.RequestId != null)
            {
                headers.Add(ApiHeaders.RequestId, callContext.RequestId);
            }

            options.AppVersion?.AddHeadersTo(headers);

            // サインアップ・サインインなど、セッションの最初の呼び出しでは日付をまだ持っていないので付けない
            if (callContext.GameDate != null)
            {
                headers.Add(ApiHeaders.GameDate, callContext.GameDate);
            }

            return headers;
        }
    }
}
