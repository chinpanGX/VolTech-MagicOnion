using System;
using System.Buffers;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MagicOnion;
using MagicOnion.Client;
using MessagePack;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// 基盤が前提にする MagicOnion のクライアントの動きを、ソースジェネレーターのクライアントで確かめる。
    /// </summary>
    /// <remarks>
    /// MagicOnion の版を上げたときに、前提が崩れていないかを見るために残す。設計書 1 の A1・A2。
    /// </remarks>
    [Description("基盤が前提にする MagicOnion のクライアントの動きを、ソースジェネレーターのクライアントで確かめる")]
    public class ClientBehaviorTests
    {
        private FakeCallInvoker invoker;
        private RecordingFilter filter;
        private IClientBehaviorService client;

        /// <summary>
        /// テストごとに、偽の CallInvoker と本物のクライアントを作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            invoker = new FakeCallInvoker { Handler = _ => Task.FromResult(SuccessResult()) };
            filter = new RecordingFilter();
            client = MagicOnionClient.Create<IClientBehaviorService>(
                invoker,
                TestClients.SerializerProvider,
                new IClientFilter[] { filter },
                TestClients.ClientFactoryProvider);
        }

        /// <summary>
        /// 本物のクライアントが偽の CallInvoker の応答を読める。
        /// </summary>
        [UnityTest]
        [Description("本物のクライアントが偽の CallInvoker の応答を読める")]
        public IEnumerator ReadsResponseFromFakeInvoker() => UniTask.ToCoroutine(async () =>
        {
            var reply = await client.GetAsync(1);

            Assert.That(reply.Value, Is.EqualTo(7));
        });

        /// <summary>
        /// MethodPath は "サービス/メソッド" の形。
        /// </summary>
        [UnityTest]
        [Description("MethodPath は サービス/メソッド の形")]
        public IEnumerator MethodPathIsServiceAndMethod() => UniTask.ToCoroutine(async () =>
        {
            await client.GetAsync(1);

            Assert.That(filter.MethodPath, Is.EqualTo("IClientBehaviorService/GetAsync"));
        });

        /// <summary>
        /// CallInvoker の TRequest は、参照型のリクエストならその型。Marshaller で往復できる。Monitor の記録の前提(設計書 4 の A1〜A3)。
        /// </summary>
        [UnityTest]
        [Description("CallInvoker の TRequest は、参照型のリクエストならその型。Marshaller で往復できる")]
        public IEnumerator RequestOfReferenceTypeIsPassedAsIs() => UniTask.ToCoroutine(async () =>
        {
            var capture = new CapturingInterceptor();

            await CreateCapturingClient(capture).SendAsync(new ClientBehaviorRequest { Value = 5 });

            Assert.That(capture.RequestType, Is.EqualTo(typeof(ClientBehaviorRequest)));
            Assert.That(((ClientBehaviorRequest)capture.RoundTripped).Value, Is.EqualTo(5));
        });

        /// <summary>
        /// 引数なしの RPC の TRequest は Box&lt;Nil&gt;。
        /// </summary>
        [UnityTest]
        [Description("引数なしの RPC の TRequest は Box<Nil>")]
        public IEnumerator RequestOfNoArgumentsIsBoxOfNil() => UniTask.ToCoroutine(async () =>
        {
            var capture = new CapturingInterceptor();
            invoker.Handler = _ => Task.FromResult(FakeResult.Success(MessagePackSerializer.Serialize(3, TestClients.SerializerOptions)));

            await CreateCapturingClient(capture).CountAsync();

            Assert.That(capture.RequestType.FullName, Does.StartWith("MagicOnion.Internal.Box`1"));
            Assert.That(capture.RequestType.GetGenericArguments()[0], Is.EqualTo(typeof(Nil)));
            Assert.That(ReadField(capture.RoundTripped, "Value"), Is.EqualTo(Nil.Default));
        });

        /// <summary>
        /// 値型 1 つの RPC の TRequest は Box&lt;T&gt;。
        /// </summary>
        [UnityTest]
        [Description("値型 1 つの RPC の TRequest は Box<T>")]
        public IEnumerator RequestOfValueTypeIsBox() => UniTask.ToCoroutine(async () =>
        {
            var capture = new CapturingInterceptor();

            await CreateCapturingClient(capture).GetAsync(5);

            Assert.That(capture.RequestType.FullName, Does.StartWith("MagicOnion.Internal.Box`1"));
            Assert.That(ReadField(capture.RoundTripped, "Value"), Is.EqualTo(5));
        });

        /// <summary>
        /// 複数の引数の RPC の TRequest は Box&lt;DynamicArgumentTuple&gt;。引数の名前は残らない。
        /// </summary>
        [UnityTest]
        [Description("複数の引数の RPC の TRequest は Box<DynamicArgumentTuple>")]
        public IEnumerator RequestOfSeveralArgumentsIsBoxOfTuple() => UniTask.ToCoroutine(async () =>
        {
            var capture = new CapturingInterceptor();

            await CreateCapturingClient(capture).TwoAsync("name", 5);

            Assert.That(capture.RequestType.FullName, Does.StartWith("MagicOnion.Internal.Box`1"));
            var tuple = ReadField(capture.RoundTripped, "Value");
            Assert.That(tuple.GetType().FullName, Does.StartWith("MagicOnion.DynamicArgumentTuple`2"));
            Assert.That(ReadField(tuple, "Item1"), Is.EqualTo("name"));
            Assert.That(ReadField(tuple, "Item2"), Is.EqualTo(5));
        });

        /// <summary>
        /// await の後に next を呼べて、next を複数回呼ぶと再送になる。
        /// </summary>
        [UnityTest]
        [Description("await の後に next を呼べて、next を複数回呼ぶと再送になる")]
        public IEnumerator NextCanBeCalledAgainAfterAwait() => UniTask.ToCoroutine(async () =>
        {
            filter.YieldBeforeNext = true;
            filter.RetryOnUnauthenticated = true;
            invoker.Handler = call => Task.FromResult(call.Index == 0
                ? FakeResult.Failure(new RpcException(new Status(StatusCode.Unauthenticated, "expired")))
                : SuccessResult());

            var reply = await client.GetAsync(1);

            Assert.That(reply.Value, Is.EqualTo(7));
            Assert.That(invoker.Calls.Count, Is.EqualTo(2));
        });

        /// <summary>
        /// next を呼ぶと、その呼び出しの中で CallInvoker まで進む。await の前に CallInvoker は呼ばれ終わっている。
        /// </summary>
        [UnityTest]
        [Description("next を呼ぶと、その呼び出しの中で CallInvoker まで進む")]
        public IEnumerator NextReachesInvokerBeforeItIsAwaited() => UniTask.ToCoroutine(async () =>
        {
            filter.YieldBeforeNext = true;
            filter.CallsAfterNext = () => invoker.Calls.Count;

            await client.GetAsync(1);

            Assert.That(filter.CallsSeenBeforeAwait, Is.EqualTo(1));
        });

        /// <summary>
        /// 失敗は await next(context) が RpcException で投げる。
        /// </summary>
        [UnityTest]
        [Description("失敗は await next(context) が RpcException で投げる")]
        public IEnumerator FailureIsThrownByNext() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = _ => Task.FromResult(FakeResult.Failure(new RpcException(new Status(StatusCode.FailedPrecondition, "failed"))));

            var exception = await CatchAsync(async () => await client.GetAsync(1));

            Assert.That(exception, Is.TypeOf<RpcException>());
            Assert.That(filter.ExceptionFromNext, Is.TypeOf<RpcException>());
        });

        /// <summary>
        /// フィルターが投げた例外は、型を保ったまま呼び出し側に届く。
        /// </summary>
        [UnityTest]
        [Description("フィルターが投げた例外は、型を保ったまま呼び出し側に届く")]
        public IEnumerator FilterExceptionKeepsItsType() => UniTask.ToCoroutine(async () =>
        {
            filter.ExceptionToThrow = new FilterFailedException();

            var exception = await CatchAsync(async () => await client.GetAsync(1));

            Assert.That(exception, Is.TypeOf<FilterFailedException>());
        });

        /// <summary>
        /// 成功なら ResponseContext からレスポンスヘッダーを読める。
        /// </summary>
        [UnityTest]
        [Description("成功なら ResponseContext からレスポンスヘッダーを読める")]
        public IEnumerator ResponseHeadersCanBeRead() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = _ => Task.FromResult(SuccessResult(new Metadata { { "x-server-time", "1" } }));

            await client.GetAsync(1);

            Assert.That(filter.ResponseHeaders.GetValue("x-server-time"), Is.EqualTo("1"));
        });

        /// <summary>
        /// Metadata はクライアントごとに使い回される。呼び出しごとの写しではない。
        /// </summary>
        [UnityTest]
        [Description("Metadata はクライアントごとに使い回される")]
        public IEnumerator HeadersAreSharedBetweenCalls() => UniTask.ToCoroutine(async () =>
        {
            await client.GetAsync(1);
            var first = filter.Headers;
            await client.GetAsync(1);

            Assert.That(filter.Headers, Is.SameAs(first));
        });

        /// <summary>
        /// WithHeaders を 2 回つなぐと、後の指定だけが残る。
        /// </summary>
        [UnityTest]
        [Description("WithHeaders を 2 回つなぐと、後の指定だけが残る")]
        public IEnumerator SecondWithHeadersReplacesTheFirst() => UniTask.ToCoroutine(async () =>
        {
            await client
                .WithHeaders(new Metadata { { "x-first", "1" } })
                .WithHeaders(new Metadata { { "x-second", "2" } })
                .GetAsync(1);

            Assert.That(filter.Headers.Select(entry => entry.Key), Is.EqualTo(new[] { "x-second" }));
        });

        /// <summary>
        /// WithHeaders で複製したクライアントも、フィルターを通る。
        /// </summary>
        [UnityTest]
        [Description("WithHeaders で複製したクライアントも、フィルターを通る")]
        public IEnumerator ClonedClientKeepsFilters() => UniTask.ToCoroutine(async () =>
        {
            await client.WithHeaders(new Metadata { { "x-first", "1" } }).GetAsync(1);

            Assert.That(filter.CallCount, Is.EqualTo(1));
        });

        /// <summary>
        /// CallInvoker の TResponse は、参照型のレスポンスならその型になる。
        /// </summary>
        [UnityTest]
        [Description("CallInvoker の TResponse は、参照型のレスポンスならその型になる")]
        public IEnumerator InvokerSeesResponseType() => UniTask.ToCoroutine(async () =>
        {
            await client.GetAsync(1);

            Assert.That(invoker.Calls[0].ResponseType, Is.EqualTo(typeof(ClientBehaviorReply)));
        });

        /// <summary>
        /// 値型のレスポンスの RPC では、CallInvoker の TResponse は MagicOnion の内部の Box になる。
        /// </summary>
        [UnityTest]
        [Description("値型のレスポンスの RPC では、CallInvoker の TResponse は MagicOnion の内部の Box になる")]
        public IEnumerator ValueTypeResponseIsBoxed() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = _ => Task.FromResult(FakeResult.Success(MessagePackSerializer.Serialize(5, TestClients.SerializerOptions)));

            var value = await client.CountAsync();

            Assert.That(value, Is.EqualTo(5));
            Assert.That(filter.ResponseType, Is.EqualTo(typeof(int)));
            Assert.That(invoker.Calls[0].ResponseType.Name, Does.StartWith("Box"));
        });

        /// <summary>
        /// 戻り値なしの RPC の ResponseType は Nil で、CallInvoker の TResponse は Box になる。
        /// </summary>
        [UnityTest]
        [Description("戻り値なしの RPC の ResponseType は Nil で、CallInvoker の TResponse は Box になる")]
        public IEnumerator NoResponseIsBoxedNil() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = _ => Task.FromResult(FakeResult.Success(MessagePackSerializer.Serialize(Nil.Default, TestClients.SerializerOptions)));

            await client.PingAsync();

            Assert.That(filter.ResponseType, Is.EqualTo(typeof(Nil)));
            Assert.That(invoker.Calls[0].ResponseType.Name, Does.StartWith("Box"));
        });

        /// <summary>
        /// GetResponseAs&lt;object&gt; は例外にならず null を返す。フィルターはレスポンスを読めない。
        /// </summary>
        [UnityTest]
        [Description("GetResponseAs<object> は例外にならず null を返す")]
        public IEnumerator FilterCannotReadResponseAsObject() => UniTask.ToCoroutine(async () =>
        {
            await client.GetAsync(1);

            Assert.That(filter.ResponseAsObject, Is.Null);
        });

        /// <summary>
        /// 生成の属性に書いた型のアセンブリの共有インターフェースは、全部クライアントが生成される。
        /// </summary>
        [Test]
        [Description("生成の属性に書いた型のアセンブリの共有インターフェースは、全部クライアントが生成される")]
        public void GenerationCoversTheWholeAssembly()
        {
            var created = MagicOnionClient.Create<INotListedService>(
                invoker,
                TestClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                TestClients.ClientFactoryProvider);

            Assert.That(client.GetType().FullName, Does.Contain("MagicOnionGeneratedClient"));
            Assert.That(created.GetType().FullName, Does.Contain("MagicOnionGeneratedClient"));
        }

        /// <summary>
        /// 生成の対象でないアセンブリの共有インターフェース(ここでは IDeviceService)は、クライアントを作るときに失敗する。
        /// </summary>
        [Test]
        [Description("生成の対象でないアセンブリの共有インターフェースは、クライアントを作るときに失敗する")]
        public void InterfaceOfOtherAssemblyFailsOnCreate()
        {
            Assert.Throws<NotSupportedException>(() => MagicOnionClient.Create<IDeviceService>(
                invoker,
                TestClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                TestClients.ClientFactoryProvider));
        }

        private static FakeResult SuccessResult(Metadata responseHeaders = null)
        {
            return FakeResult.Success(
                MessagePackSerializer.Serialize(new ClientBehaviorReply { Value = 7 }, TestClients.SerializerOptions),
                responseHeaders);
        }

        private static async Task<Exception> CatchAsync(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static object ReadField(object value, string name)
        {
            return value.GetType().GetField(name).GetValue(value);
        }

        private IClientBehaviorService CreateCapturingClient(CapturingInterceptor capture)
        {
            return MagicOnionClient.Create<IClientBehaviorService>(
                invoker.Intercept(capture),
                TestClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                TestClients.ClientFactoryProvider);
        }

        // TRequest と、Marshaller で往復した値を捕まえる
        private sealed class CapturingInterceptor : Interceptor
        {
            public Type RequestType { get; private set; }

            public object RoundTripped { get; private set; }

            public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
                TRequest request,
                ClientInterceptorContext<TRequest, TResponse> context,
                AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
            {
                RequestType = typeof(TRequest);
                var serialization = new BufferSerializationContext();
                context.Method.RequestMarshaller.ContextualSerializer(request, serialization);
                RoundTripped = context.Method.RequestMarshaller.ContextualDeserializer(new BufferDeserializationContext(serialization.Payload));
                return continuation(request, context);
            }
        }

        private sealed class BufferSerializationContext : SerializationContext
        {
            private readonly ArrayBufferWriter<byte> writer = new();

            public byte[] Payload { get; private set; }

            public override IBufferWriter<byte> GetBufferWriter()
            {
                return writer;
            }

            public override void SetPayloadLength(int payloadLength)
            {
            }

            public override void Complete()
            {
                Payload = writer.WrittenSpan.ToArray();
            }

            public override void Complete(byte[] payload)
            {
                Payload = payload;
            }
        }

        private sealed class BufferDeserializationContext : DeserializationContext
        {
            private readonly byte[] payload;

            public BufferDeserializationContext(byte[] payload)
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

        private sealed class FilterFailedException : Exception
        {
        }

        // 確かめたい動きを記録するフィルター
        private sealed class RecordingFilter : IClientFilter
        {
            public int CallCount { get; private set; }

            public string MethodPath { get; private set; }

            public Metadata Headers { get; private set; }

            public Metadata ResponseHeaders { get; private set; }

            public Type ResponseType { get; private set; }

            public object ResponseAsObject { get; private set; }

            public Exception ExceptionFromNext { get; private set; }

            public int CallsSeenBeforeAwait { get; private set; }

            public bool YieldBeforeNext { get; set; }

            public bool RetryOnUnauthenticated { get; set; }

            public Func<int> CallsAfterNext { get; set; }

            public Exception ExceptionToThrow { get; set; }

            public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
            {
                CallCount++;
                MethodPath = context.MethodPath;
                ResponseType = context.ResponseType;
                Headers = context.CallOptions.Headers;
                if (YieldBeforeNext)
                {
                    await Task.Yield();
                }

                while (true)
                {
                    try
                    {
                        var pending = next(context);
                        CallsSeenBeforeAwait = CallsAfterNext?.Invoke() ?? 0;
                        var response = await pending;
                        ResponseHeaders = await response.ResponseHeadersAsync;
                        ResponseAsObject = await response.GetResponseAs<object>();
                        if (ExceptionToThrow != null)
                        {
                            throw ExceptionToThrow;
                        }

                        return response;
                    }
                    catch (RpcException exception)
                    {
                        ExceptionFromNext = exception;
                        if (RetryOnUnauthenticated && exception.StatusCode == StatusCode.Unauthenticated)
                        {
                            continue;
                        }

                        throw;
                    }
                }
            }
        }
    }

    public interface IClientBehaviorService : IService<IClientBehaviorService>
    {
        UnaryResult<ClientBehaviorReply> GetAsync(int value);

        UnaryResult<int> CountAsync();

        UnaryResult PingAsync();

        UnaryResult<ClientBehaviorReply> SendAsync(ClientBehaviorRequest request);

        UnaryResult<ClientBehaviorReply> TwoAsync(string name, int value);
    }

    /// <summary>[MagicOnionClientGeneration] に名前を書かないインターフェース。同じアセンブリなので生成される。</summary>
    public interface INotListedService : IService<INotListedService>
    {
        UnaryResult<ClientBehaviorReply> GetAsync(int value);
    }

    [MessagePackObject]
    public sealed class ClientBehaviorRequest
    {
        [Key(0)]
        public int Value { get; set; }
    }

    [MessagePackObject]
    public sealed class ClientBehaviorReply
    {
        [Key(0)]
        public int Value { get; set; }
    }
}
