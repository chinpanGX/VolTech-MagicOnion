using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MagicOnion.Client;
using MessagePack;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Client.Editor;
using VolTech.Client.Unity;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// Monitor のための記録(ApiTraceInterceptor・ApiTraceStore)を、サーバーなしで確かめる。
    /// </summary>
    [Description("Monitor のための記録(ApiTraceInterceptor・ApiTraceStore)を、サーバーなしで確かめる")]
    public class ApiTraceTests
    {
        private ApiTraceStore store;
        private FakeCallInvoker invoker;
        private IClientBehaviorService client;

        /// <summary>
        /// テストごとに、空の記録の置き場所と、それに記録する本物のクライアントを作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            store = new ApiTraceStore();
            invoker = new FakeCallInvoker { Handler = _ => Task.FromResult(ReplyResult()) };
            client = MagicOnionClient.Create<IClientBehaviorService>(
                invoker.Intercept(new ApiTraceInterceptor(store)),
                TestClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                TestClients.ClientFactoryProvider);
        }

        /// <summary>
        /// 上限を超えたら、古いものから捨てる。
        /// </summary>
        [Test]
        [Description("上限を超えたら、古いものから捨てる")]
        public void StoreDropsOldestOverCapacity()
        {
            for (var index = 0; index <= ApiTraceStore.Capacity; index++)
            {
                store.Add(new ApiTrace { Id = store.NextId() });
            }

            var traces = store.Snapshot();
            Assert.That(traces.Length, Is.EqualTo(ApiTraceStore.Capacity));
            Assert.That(traces[0].Id, Is.EqualTo(2));
        }

        /// <summary>
        /// 置き換えると版が増える。消した記録は、結果の置き換えで戻らない。
        /// </summary>
        [Test]
        [Description("置き換えると版が増える。消した記録は、結果の置き換えで戻らない")]
        public void UpdateDoesNotBringBackClearedTrace()
        {
            var trace = new ApiTrace { Id = store.NextId() };
            store.Add(trace);
            var version = store.Version;

            Assert.That(store.Update(trace.Clone()), Is.True);
            Assert.That(store.Version, Is.GreaterThan(version));

            store.Clear();

            Assert.That(store.Update(trace.Clone()), Is.False);
            Assert.That(store.Snapshot(), Is.Empty);
        }

        /// <summary>
        /// 成功した通信は、ヘッダー・Body・ステータス・所要時間が記録され、レスポンスはそのまま返る。
        /// </summary>
        [UnityTest]
        [Description("成功した通信は、ヘッダー・Body・ステータス・所要時間が記録され、レスポンスはそのまま返る")]
        public IEnumerator SuccessIsRecorded() => UniTask.ToCoroutine(async () =>
        {
            var responseHeaders = new Metadata { { "x-game-date", "2026-11-08" } };
            invoker.Handler = _ => Task.FromResult(ReplyResult(responseHeaders));

            var reply = await client
                .WithHeaders(new Metadata { { "authorization", "Bearer token-1" } })
                .SendAsync(new ClientBehaviorRequest { Value = 5 });

            Assert.That(reply.Value, Is.EqualTo(7));
            var trace = store.Snapshot().Single();
            Assert.That(trace.Method, Is.EqualTo("/IClientBehaviorService/SendAsync"));
            Assert.That(trace.ShortMethod, Is.EqualTo("SendAsync"));
            Assert.That(trace.State, Is.EqualTo(ApiTraceState.Succeeded));
            Assert.That(trace.StatusCode, Is.EqualTo("OK"));
            Assert.That(trace.DurationMs, Is.GreaterThanOrEqualTo(0));
            Assert.That(trace.RequestHeaders.Single(header => header.Key == "authorization").Value, Is.EqualTo("Bearer token-1"));
            Assert.That(trace.ResponseHeaders.Single(header => header.Key == "x-game-date").Value, Is.EqualTo("2026-11-08"));
            Assert.That(trace.RequestBody, Is.EqualTo("{\"Value\":5}"));
            Assert.That(trace.ResponseBody, Is.EqualTo("{\"Value\":7}"));
        });

        /// <summary>
        /// 業務エラーは、ステータスと ErrorInfo が記録され、呼び出し側に同じ例外が届く。
        /// </summary>
        [UnityTest]
        [Description("業務エラーは、ステータスと ErrorInfo が記録され、呼び出し側に同じ例外が届く")]
        public IEnumerator BusinessErrorIsRecorded() => UniTask.ToCoroutine(async () =>
        {
            var metadata = new Dictionary<string, string> { { "end_at", "2026-11-09" } };
            var failure = FakeRpcErrors.FailedPrecondition(new ErrorCode("sample", "NOT_ENOUGH_GEMS"), "ジェムが足りない", metadata);
            invoker.Handler = _ => Task.FromResult(FakeResult.Failure(failure));

            var caught = await CatchAsync(() => client.GetAsync(1).ResponseAsync);

            Assert.That(caught, Is.InstanceOf<RpcException>());
            var trace = store.Snapshot().Single();
            Assert.That(trace.State, Is.EqualTo(ApiTraceState.Failed));
            Assert.That(trace.StatusCode, Is.EqualTo("FailedPrecondition"));
            Assert.That(trace.StatusDetail, Is.EqualTo("ジェムが足りない"));
            Assert.That(trace.ErrorReason, Is.EqualTo("NOT_ENOUGH_GEMS"));
            Assert.That(trace.ErrorDomain, Is.EqualTo("sample"));
            Assert.That(trace.ErrorMetadata.Single().Key, Is.EqualTo("end_at"));
            Assert.That(trace.ResponseBody, Is.Null);
        });

        /// <summary>
        /// ErrorInfo の無い通信エラーも記録される。
        /// </summary>
        [UnityTest]
        [Description("ErrorInfo の無い通信エラーも記録される")]
        public IEnumerator NetworkErrorIsRecorded() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = _ => Task.FromResult(FakeResult.Failure(FakeRpcErrors.Status(StatusCode.Unavailable, "down")));

            var caught = await CatchAsync(() => client.GetAsync(1).ResponseAsync);

            Assert.That(caught, Is.InstanceOf<RpcException>());
            var trace = store.Snapshot().Single();
            Assert.That(trace.StatusCode, Is.EqualTo("Unavailable"));
            Assert.That(trace.ErrorReason, Is.Null.Or.Empty);
        });

        /// <summary>
        /// 応答を待っている間は、送信中の記録が見える。
        /// </summary>
        [UnityTest]
        [Description("応答を待っている間は、送信中の記録が見える")]
        public IEnumerator PendingTraceIsVisible() => UniTask.ToCoroutine(async () =>
        {
            var held = new TaskCompletionSource<FakeResult>();
            invoker.Handler = _ => held.Task;

            var result = client.GetAsync(1);

            var pending = store.Snapshot().Single();
            Assert.That(pending.State, Is.EqualTo(ApiTraceState.Pending));
            Assert.That(pending.DurationMs, Is.EqualTo(-1));

            held.SetResult(ReplyResult());
            await result;

            Assert.That(store.Snapshot().Single().State, Is.EqualTo(ApiTraceState.Succeeded));
        });

        /// <summary>
        /// 通信の後にオブジェクトを書き換えても、Body は通信の時点の中身のまま。
        /// </summary>
        [UnityTest]
        [Description("通信の後にオブジェクトを書き換えても、Body は通信の時点の中身のまま")]
        public IEnumerator BodyKeepsContentAtCallTime() => UniTask.ToCoroutine(async () =>
        {
            var request = new ClientBehaviorRequest { Value = 5 };

            var reply = await client.SendAsync(request);
            request.Value = 100;
            reply.Value = 200;

            var trace = store.Snapshot().Single();
            Assert.That(trace.RequestBody, Is.EqualTo("{\"Value\":5}"));
            Assert.That(trace.ResponseBody, Is.EqualTo("{\"Value\":7}"));
        });

        /// <summary>
        /// 送る処理が同期で例外を投げたら、記録を失敗にして、同じ例外を投げる。
        /// </summary>
        [Test]
        [Description("送る処理が同期で例外を投げたら、記録を失敗にして、同じ例外を投げる")]
        public void SynchronousExceptionIsRecorded()
        {
            var failure = new InvalidOperationException("disposed");
            var interceptor = new ApiTraceInterceptor(store);

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                interceptor.AsyncUnaryCall("request", CreateContext(Marshallers.StringMarshaller), (_, _) => throw failure));

            Assert.That(thrown, Is.SameAs(failure));
            var trace = store.Snapshot().Single();
            Assert.That(trace.State, Is.EqualTo(ApiTraceState.Failed));
            Assert.That(trace.StatusCode, Is.EqualTo(nameof(InvalidOperationException)));
            Assert.That(trace.StatusDetail, Is.EqualTo("disposed"));
        }

        /// <summary>
        /// Body をシリアライズできなくても、通信の結果は変わらない。
        /// </summary>
        [UnityTest]
        [Description("Body をシリアライズできなくても、通信の結果は変わらない")]
        public IEnumerator SerializeFailureDoesNotAffectCall() => UniTask.ToCoroutine(async () =>
        {
            var broken = Marshallers.Create<string>(_ => throw new InvalidOperationException("broken"), _ => "");
            var interceptor = new ApiTraceInterceptor(store);

            var call = interceptor.AsyncUnaryCall("request", CreateContext(broken), (_, _) => CreateCall(Task.FromResult("response")));

            Assert.That(await call.ResponseAsync, Is.EqualTo("response"));
            var trace = store.Snapshot().Single();
            Assert.That(trace.State, Is.EqualTo(ApiTraceState.Succeeded));
            Assert.That(trace.RequestBody, Does.StartWith("(could not serialize: "));
            Assert.That(trace.ResponseBody, Does.StartWith("(could not serialize: "));
        });

        /// <summary>
        /// Recording が false の間は記録しない。
        /// </summary>
        [UnityTest]
        [Description("Recording が false の間は記録しない")]
        public IEnumerator DoesNotRecordWhilePaused() => UniTask.ToCoroutine(async () =>
        {
            store.Recording = false;

            await client.GetAsync(1);

            Assert.That(store.Snapshot(), Is.Empty);
        });

        /// <summary>
        /// 同期コンテキストのスレッドが止まっていても、記録の処理は終わる。ReadResponse の .Result で固まらない。
        /// </summary>
        [Test]
        [Description("同期コンテキストのスレッドが止まっていても、記録の処理は終わる")]
        public void RecordingDoesNotNeedSynchronizationContext()
        {
            Assert.That(SynchronizationContext.Current, Is.Not.Null);
            var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var interceptor = new ApiTraceInterceptor(store);

            var call = interceptor.AsyncUnaryCall("request", CreateContext(Marshallers.StringMarshaller), (_, _) => CreateCall(held.Task));
            Task.Run(() => held.SetResult("response"));

            // 不具合が戻ったときに Editor を固めないように、.Result ではなくタイムアウト付きで待つ
            Assert.That(call.ResponseAsync.Wait(TimeSpan.FromSeconds(5)), Is.True);
        }

        /// <summary>
        /// ApiCore の再認証の送り直しは 2 行になり、基盤が付けたヘッダーと締め切りが見える。冪等性の対象なら x-request-id が同じ。
        /// </summary>
        [UnityTest]
        [Description("ApiCore の再認証の送り直しは 2 行になり、基盤が付けたヘッダーと締め切りが見える")]
        public IEnumerator RetryOfApiCoreIsRecordedTwice() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Task.FromResult(call.Index == 0
                ? FakeResult.Failure(FakeRpcErrors.Status(StatusCode.Unauthenticated, "expired"))
                : FakeResult.Success(MessagePackSerializer.Serialize("ok", TestClients.SerializerOptions)));
            var core = ApiCore.CreateBuilder(
                    invoker.Intercept(new ApiTraceInterceptor(store)),
                    TestClients.SerializerProvider,
                    TestClients.ClientFactoryProvider,
                    new ApiCoreOptions(),
                    new FakeTokenProvider(),
                    new ErrorPolicyProvider(Array.Empty<ErrorDefinition>()),
                    new FakeErrorHandler())
                .WithCommonResponseApplier(new FakeResponseApplier())
                .Build();

            var result = await core.CreateClient<ITestService>().WriteAsync("value").ToResult();

            Assert.That(result.IsError, Is.False);
            var traces = store.Snapshot();
            Assert.That(traces.Select(trace => trace.StatusCode), Is.EqualTo(new[] { "Unauthenticated", "OK" }));
            var requestIds = traces.Select(trace => trace.RequestHeaders.Single(header => header.Key == ApiHeaders.RequestId).Value).ToArray();
            Assert.That(requestIds[1], Is.EqualTo(requestIds[0]));
            Assert.That(traces[1].RequestHeaders.Single(header => header.Key == ApiHeaders.Authorization).Value, Is.EqualTo("Bearer token-2"));
            Assert.That(traces[1].Deadline, Is.Not.Empty);
            Assert.That(traces[0].RequestHeaders.Select(header => header.Key), Has.None.Contains("x-voltech-call"));
        });

        /// <summary>
        /// ApiChannel.CreateCallInvoker で作った CallInvoker の通信は、ApiMonitorStartup が付けた Interceptor で記録される。
        /// </summary>
        [UnityTest]
        [Description("ApiChannel.CreateCallInvoker で作った CallInvoker の通信は記録される")]
        public IEnumerator ChannelCallsAreRecorded() => UniTask.ToCoroutine(async () =>
        {
            var shared = ApiTraceStore.Shared;
            var recording = shared.Recording;
            shared.Recording = true;
            try
            {
                var lastId = shared.Snapshot().Select(trace => trace.Id).DefaultIfEmpty(0).Max();
                using var channel = ApiChannel.Create("http://127.0.0.1:1", TimeSpan.FromSeconds(1));
                var channelClient = MagicOnionClient.Create<IClientBehaviorService>(
                    ApiChannel.CreateCallInvoker(channel),
                    TestClients.SerializerProvider,
                    Array.Empty<IClientFilter>(),
                    TestClients.ClientFactoryProvider);

                var caught = await CatchAsync(() => channelClient.GetAsync(1).ResponseAsync);

                Assert.That(caught, Is.InstanceOf<RpcException>());
                var deadline = DateTime.UtcNow.AddSeconds(5);
                ApiTrace recorded;
                do
                {
                    await UniTask.Yield();
                    recorded = shared.Snapshot().FirstOrDefault(trace => trace.Id > lastId && trace.State == ApiTraceState.Failed);
                }
                while (recorded == null && DateTime.UtcNow < deadline);

                Assert.That(recorded?.Method, Is.EqualTo("/IClientBehaviorService/GetAsync"));
            }
            finally
            {
                shared.Recording = recording;
            }
        });

        private static async UniTask<Exception> CatchAsync(Func<Task> call)
        {
            try
            {
                await call();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static FakeResult ReplyResult(Metadata responseHeaders = null)
        {
            var reply = new ClientBehaviorReply { Value = 7 };
            return FakeResult.Success(MessagePackSerializer.Serialize(reply, TestClients.SerializerOptions), responseHeaders);
        }

        private static ClientInterceptorContext<string, string> CreateContext(Marshaller<string> marshaller)
        {
            var method = new Method<string, string>(MethodType.Unary, "Service", "Method", marshaller, marshaller);
            return new ClientInterceptorContext<string, string>(method, null, new CallOptions());
        }

        private static AsyncUnaryCall<string> CreateCall(Task<string> response)
        {
            return new AsyncUnaryCall<string>(
                response,
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        // 呼ばれるたびに token-1、token-2 と新しいトークンを返す
        private sealed class FakeTokenProvider : ITokenProvider
        {
            private int issued;
            private string current;

            public UniTask<string> GetTokenAsync()
            {
                current ??= $"token-{++issued}";
                return UniTask.FromResult(current);
            }

            public void Invalidate(string rejectedToken)
            {
                current = null;
            }

            public void Reset()
            {
                current = null;
            }
        }

        private sealed class FakeErrorHandler : ICommonErrorHandler
        {
            public UniTask<ErrorDecision> HandleAsync(ApiError error)
            {
                return UniTask.FromResult(ErrorDecision.Stay);
            }
        }

        // 反映処理があると、ApiCore が ReadResponse でレスポンスを読む
        private sealed class FakeResponseApplier : ICommonResponseApplier<string>
        {
            public UniTask ApplyAsync(string common)
            {
                return UniTask.CompletedTask;
            }
        }
    }
}
