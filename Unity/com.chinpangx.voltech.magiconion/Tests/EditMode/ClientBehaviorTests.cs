using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Core;
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
    }

    /// <summary>[MagicOnionClientGeneration] に名前を書かないインターフェース。同じアセンブリなので生成される。</summary>
    public interface INotListedService : IService<INotListedService>
    {
        UnaryResult<ClientBehaviorReply> GetAsync(int value);
    }

    [MessagePackObject]
    public sealed class ClientBehaviorReply
    {
        [Key(0)]
        public int Value { get; set; }
    }
}
