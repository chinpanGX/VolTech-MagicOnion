using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// 通信する代わりに、テストが渡した処理で応答を返す CallInvoker。本物の MagicOnionClient の内側に置く。
    /// </summary>
    /// <remarks>
    /// MagicOnion は内部の型で通信するので、型付きの値ではなく MessagePack のバイト列で応答を渡す。
    /// Handler は Task を返すので、TaskCompletionSource で応答を保留できる。
    /// </remarks>
    public sealed class FakeCallInvoker : CallInvoker
    {
        private readonly List<FakeCall> calls = new();

        /// <summary>呼ばれた順の記録。キャンセル済みで Handler を呼ばなかった呼び出しも残す。</summary>
        public IReadOnlyList<FakeCall> Calls => calls;

        /// <summary>応答を返す処理。キャンセル済みの呼び出しでは呼ばない。</summary>
        public Func<FakeCall, Task<FakeResult>> Handler { get; set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions options,
            TRequest request)
        {
            var call = new FakeCall(method.FullName, calls.Count, typeof(TResponse), CopyHeaders(options.Headers), options);
            calls.Add(call);

            // 本物の gRPC と同じく、キャンセル済みなら通信せずに失敗する
            var result = options.CancellationToken.IsCancellationRequested
                ? Task.FromResult(FakeResult.Failure(new RpcException(new Status(StatusCode.Cancelled, "cancelled"))))
                : Handler(call);

            return new AsyncUnaryCall<TResponse>(
                ReadResponseAsync(result, method),
                ReadHeadersAsync(result),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }

        private static async Task<TResponse> ReadResponseAsync<TRequest, TResponse>(Task<FakeResult> result, Method<TRequest, TResponse> method)
        {
            var completed = await result;
            if (completed.Exception != null)
            {
                throw completed.Exception;
            }

            return method.ResponseMarshaller.ContextualDeserializer(new FakeDeserializationContext(completed.Payload));
        }

        private static async Task<Metadata> ReadHeadersAsync(Task<FakeResult> result)
        {
            var completed = await result;
            if (completed.Exception != null)
            {
                throw completed.Exception;
            }

            return completed.ResponseHeaders;
        }

        // Metadata はクライアントごとに使い回されるので、呼ばれた時点の中身を写して記録する
        private static Metadata CopyHeaders(Metadata headers)
        {
            var copy = new Metadata();
            if (headers == null)
            {
                return copy;
            }

            foreach (var entry in headers)
            {
                copy.Add(entry);
            }

            return copy;
        }

        private sealed class FakeDeserializationContext : DeserializationContext
        {
            private readonly byte[] payload;

            public FakeDeserializationContext(byte[] payload)
            {
                this.payload = payload;
            }

            public override int PayloadLength => payload.Length;

            public override byte[] PayloadAsNewBuffer()
            {
                return payload;
            }

            public override ReadOnlySequence<byte> PayloadAsReadOnlySequence()
            {
                return new ReadOnlySequence<byte>(payload);
            }
        }
    }

    /// <summary>
    /// FakeCallInvoker が受けた 1 回の呼び出し。
    /// </summary>
    public sealed class FakeCall
    {
        public FakeCall(string methodFullName, int index, Type responseType, Metadata headers, CallOptions options)
        {
            MethodFullName = methodFullName;
            Index = index;
            ResponseType = responseType;
            Headers = headers;
            Options = options;
        }

        /// <summary>"/サービス/メソッド" の形の名前。</summary>
        public string MethodFullName { get; }

        /// <summary>0 から数えた呼び出しの順番。</summary>
        public int Index { get; }

        /// <summary>CallInvoker の TResponse。</summary>
        public Type ResponseType { get; }

        /// <summary>呼ばれた時点のヘッダーの写し。</summary>
        public Metadata Headers { get; }

        public CallOptions Options { get; }
    }

    /// <summary>
    /// FakeCallInvoker が返す応答。
    /// </summary>
    public sealed class FakeResult
    {
        private FakeResult(byte[] payload, Metadata responseHeaders, Exception exception)
        {
            Payload = payload;
            ResponseHeaders = responseHeaders;
            Exception = exception;
        }

        public byte[] Payload { get; }

        public Metadata ResponseHeaders { get; }

        public Exception Exception { get; }

        /// <summary>
        /// 成功の応答を作る。
        /// </summary>
        /// <param name="payload">レスポンスを MessagePack にしたバイト列。</param>
        /// <param name="responseHeaders">レスポンスヘッダー。null なら空。</param>
        public static FakeResult Success(byte[] payload, Metadata responseHeaders = null)
        {
            return new FakeResult(payload, responseHeaders ?? new Metadata(), null);
        }

        /// <summary>
        /// 失敗の応答を作る。
        /// </summary>
        public static FakeResult Failure(Exception exception)
        {
            return new FakeResult(null, null, exception);
        }
    }
}
