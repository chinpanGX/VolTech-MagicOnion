using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// ApiCore と ApiServiceBase が、MagicOnion のクライアントで、ヘッダー・RPC ごとの設定・再認証・エラーの変換を仕様どおりに行うことを確かめる。
    /// </summary>
    [Description("ApiCore と ApiServiceBase が、MagicOnion のクライアントでヘッダー・RPC ごとの設定・再認証・エラーの変換を仕様どおりに行うことを確かめる")]
    public class ApiCoreTests
    {
        private FakeCallInvoker invoker;
        private FakeTokenProvider tokenProvider;
        private RecordingErrorHandler errorHandler;
        private ServerClock serverClock;
        private ApiCore core;
        private TestApi api;

        /// <summary>
        /// テストごとに、成功を返す偽のサービスと、それを呼ぶ ApiCore を作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            invoker = new FakeCallInvoker { Handler = call => Success(MethodOf(call) + ":ok") };
            tokenProvider = new FakeTokenProvider();
            errorHandler = new RecordingErrorHandler();
            serverClock = new ServerClock(() => TimeSpan.Zero);
            core = ApiCore.CreateBuilder(
                    new ApiCoreOptions(),
                    tokenProvider,
                    new ErrorPolicyProvider(new[] { new ErrorDefinition(TestErrors.NotEnough, ErrorAction.Stay) }),
                    errorHandler)
                .WithServerClock(serverClock)
                .Build();
            api = new TestApi(core, MagicOnionClient.Create<ITestService>(
                invoker,
                TestClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                TestClients.ClientFactoryProvider));
        }

        /// <summary>
        /// RPC ごとの設定は共有インターフェースの属性から読む。[SkipAuth] はサービス全体に、[NoSideEffects] はメソッドに効く。
        /// </summary>
        [Test]
        [Description("RPC ごとの設定は共有インターフェースの属性から読む")]
        public void SettingsComeFromSharedInterfaceAttributes()
        {
            var device = RpcSettings.CreateAll(typeof(IDeviceService));
            var test = RpcSettings.CreateAll(typeof(ITestService));

            Assert.That(device.Values.Select(settings => settings.RequiresAuth), Is.All.False);
            Assert.That(device.Values.Select(settings => settings.IsIdempotencyTarget), Is.All.False);
            Assert.That(device[nameof(IDeviceService.RegisterAsync)].MethodName, Is.EqualTo("IDeviceService/RegisterAsync"));
            Assert.That(test[nameof(ITestService.ReadAsync)].IsIdempotencyTarget, Is.False);
            Assert.That(test[nameof(ITestService.WriteAsync)].IsIdempotencyTarget, Is.True);
            Assert.That(test[nameof(ITestService.ReadAsync)].RequiresAuth, Is.True);
            Assert.That(test[nameof(ITestService.WriteAsync)].RequiresAuth, Is.True);
            Assert.That(test[nameof(ITestService.PublicAsync)].RequiresAuth, Is.False);
            Assert.That(test[nameof(ITestService.PublicAsync)].IsIdempotencyTarget, Is.False);
        }

        /// <summary>
        /// 認証が要る RPC には authorization: Bearer を付け、冪等性の対象にだけ x-request-id を付ける。締め切りも付ける。
        /// </summary>
        [UnityTest]
        [Description("認証が要る RPC には authorization: Bearer を付け、冪等性の対象にだけ x-request-id を付ける")]
        public IEnumerator SendsAuthorizationAndRequestId() => UniTask.ToCoroutine(async () =>
        {
            var write = await api.WriteAsync("a");
            var read = await api.ReadAsync(1);

            Assert.That(write.Value, Is.EqualTo("WriteAsync:ok"));
            Assert.That(read.Value, Is.EqualTo("ReadAsync:ok"));
            var writeCall = invoker.Calls[0];
            var readCall = invoker.Calls[1];
            Assert.That(writeCall.Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-1"));
            Assert.That(RequestIdRules.IsValid(writeCall.Headers.GetValue(ApiHeaders.RequestId)), Is.True);
            Assert.That(readCall.Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-1"));
            Assert.That(readCall.Headers.GetValue(ApiHeaders.RequestId), Is.Null);
            Assert.That(writeCall.Options.Deadline, Is.Not.Null);
        });

        /// <summary>
        /// レスポンスヘッダーの x-server-time で時計を合わせ、最初に受け取った x-game-date を次のリクエストに付ける。
        /// </summary>
        [UnityTest]
        [Description("レスポンスヘッダーの x-server-time で時計を合わせ、最初に受け取った x-game-date を次のリクエストに付ける")]
        public IEnumerator AppliesResponseHeaders() => UniTask.ToCoroutine(async () =>
        {
            var serverTime = new DateTimeOffset(2026, 10, 30, 3, 0, 0, TimeSpan.Zero);
            var gameDates = new Queue<string>(new[] { "2026-10-30", "2026-10-31" });
            invoker.Handler = call => Success(
                "ok",
                new Metadata
                {
                    { ApiHeaders.ServerTime, serverTime.ToUnixTimeMilliseconds().ToString() },
                    { ApiHeaders.GameDate, gameDates.Dequeue() },
                });

            await api.ReadAsync(1);
            await api.ReadAsync(2);
            await api.ReadAsync(3);

            Assert.That(serverClock.Now, Is.EqualTo(serverTime));
            Assert.That(invoker.Calls[0].Headers.GetValue(ApiHeaders.GameDate), Is.Null);
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.GameDate), Is.EqualTo("2026-10-30"));
            Assert.That(invoker.Calls[2].Headers.GetValue(ApiHeaders.GameDate), Is.EqualTo("2026-10-30"));
        });

        /// <summary>
        /// UNAUTHENTICATED なら、トークンを取り直して同じ RequestID で1回だけ送り直す。
        /// </summary>
        [UnityTest]
        [Description("UNAUTHENTICATED なら、トークンを取り直して同じ RequestID で1回だけ送り直す")]
        public IEnumerator ResendsOnceAfterUnauthenticated() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => invoker.Calls.Count == 1
                ? Failure(FakeRpcErrors.Status(StatusCode.Unauthenticated))
                : Success("ok");

            var result = await api.WriteAsync("a");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(tokenProvider.Invalidated, Is.EqualTo(new[] { "token-1" }));
            Assert.That(invoker.Calls.Count, Is.EqualTo(2));
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-2"));
            Assert.That(
                invoker.Calls[1].Headers.GetValue(ApiHeaders.RequestId),
                Is.EqualTo(invoker.Calls[0].Headers.GetValue(ApiHeaders.RequestId)));
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 送り直した後も UNAUTHENTICATED なら、それ以上は送らず、エラーハンドラーに渡して失敗にする。
        /// </summary>
        [UnityTest]
        [Description("送り直した後も UNAUTHENTICATED なら、それ以上は送らずにエラーハンドラーに渡す")]
        public IEnumerator StopsResendingAfterSecondUnauthenticated() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.Status(StatusCode.Unauthenticated));

            var result = await api.WriteAsync("a");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(invoker.Calls.Count, Is.EqualTo(2));
            Assert.That(tokenProvider.Invalidated, Is.EqualTo(new[] { "token-1", "token-2" }));
            Assert.That(errorHandler.Errors.Select(error => error.Code), Is.EqualTo(new[] { ReservedErrors.UnknownError }));
            Assert.That(result.Error.StatusCode, Is.EqualTo(StatusCode.Unauthenticated));
        });

        /// <summary>
        /// 2 回続けて UNAUTHENTICATED の後に、エラーハンドラーが Retry を返したら、新しいトークンで同じ RequestID のまま送る。
        /// </summary>
        [UnityTest]
        [Description("2 回続けて UNAUTHENTICATED の後の Retry は、新しいトークンで同じ RequestID のまま送る")]
        public IEnumerator RetryAfterSecondUnauthenticatedUsesNewToken() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => invoker.Calls.Count <= 2
                ? Failure(FakeRpcErrors.Status(StatusCode.Unauthenticated))
                : Success("ok");
            errorHandler.Decisions.Enqueue(ErrorDecision.Retry);

            var result = await api.WriteAsync("a");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(invoker.Calls.Count, Is.EqualTo(3));
            Assert.That(invoker.Calls[2].Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-3"));
            var requestId = invoker.Calls[0].Headers.GetValue(ApiHeaders.RequestId);
            Assert.That(RequestIdRules.IsValid(requestId), Is.True);
            Assert.That(invoker.Calls.Select(call => call.Headers.GetValue(ApiHeaders.RequestId)), Is.All.EqualTo(requestId));
        });

        /// <summary>
        /// [SkipAuth] の RPC は冪等性の対象にしない。authorization も x-request-id も付けない。
        /// </summary>
        [UnityTest]
        [Description("[SkipAuth] の RPC には authorization も x-request-id も付けない")]
        public IEnumerator SkipAuthRpcSendsNoTokenOrRequestId() => UniTask.ToCoroutine(async () =>
        {
            var result = await api.PublicAsync("a");

            Assert.That(result.Value, Is.EqualTo("PublicAsync:ok"));
            Assert.That(invoker.Calls[0].Headers.GetValue(ApiHeaders.Authorization), Is.Null);
            Assert.That(invoker.Calls[0].Headers.GetValue(ApiHeaders.RequestId), Is.Null);
        });

        /// <summary>
        /// Retry の後にまた UNAUTHENTICATED なら、トークンを取り直して 1 回だけ自動で再送する。
        /// </summary>
        [UnityTest]
        [Description("Retry の後にまた UNAUTHENTICATED なら、1 回だけ自動で再送する")]
        public IEnumerator ResendsOnceAgainAfterRetry() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => invoker.Calls.Count <= 3
                ? Failure(FakeRpcErrors.Status(StatusCode.Unauthenticated))
                : Success("ok");
            errorHandler.Decisions.Enqueue(ErrorDecision.Retry);

            var result = await api.WriteAsync("a");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(invoker.Calls.Count, Is.EqualTo(4));
            Assert.That(errorHandler.Errors.Count, Is.EqualTo(1));
            Assert.That(tokenProvider.Invalidated, Is.EqualTo(new[] { "token-1", "token-2", "token-3" }));
            Assert.That(invoker.Calls[3].Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-4"));
        });

        /// <summary>
        /// [SkipAuth] の RPC は順番待ちをしない。冪等性の対象の RPC が応答を待っている間にも送れる。
        /// </summary>
        [UnityTest]
        [Description("[SkipAuth] の RPC は、冪等性の対象の RPC の応答を待たずに送れる")]
        public IEnumerator SkipAuthRpcDoesNotWaitInQueue() => UniTask.ToCoroutine(async () =>
        {
            var pendingWrite = new TaskCompletionSource<FakeResult>();
            invoker.Handler = call => MethodOf(call) == nameof(ITestService.WriteAsync)
                ? pendingWrite.Task
                : Success(MethodOf(call) + ":ok");

            var write = api.WriteAsync("a");
            var publicCall = api.PublicAsync("b");

            // 順番待ちをしていなければ、WriteAsync の応答を待たずに終わる
            var publicResult = await publicCall;
            Assert.That(write.Status, Is.EqualTo(UniTaskStatus.Pending));

            pendingWrite.SetResult(await Success("written"));
            var writeResult = await write;

            Assert.That(writeResult.Value, Is.EqualTo("written"));
            Assert.That(publicResult.Value, Is.EqualTo("PublicAsync:ok"));
        });

        /// <summary>
        /// [SkipAuth] の RPC が UNAUTHENTICATED を受けたら、トークンを取り直さず、UNKNOWN_ERROR としてエラーハンドラーに渡す。
        /// </summary>
        [UnityTest]
        [Description("[SkipAuth] の RPC の UNAUTHENTICATED は、取り直さずに UNKNOWN_ERROR としてエラーハンドラーに渡す")]
        public IEnumerator SkipAuthRpcUnauthenticatedGoesToErrorHandler() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.Status(StatusCode.Unauthenticated));

            var result = await api.PublicAsync("a");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(invoker.Calls.Count, Is.EqualTo(1));
            Assert.That(tokenProvider.Invalidated, Is.Empty);
            Assert.That(errorHandler.Errors.Select(error => error.Code), Is.EqualTo(new[] { ReservedErrors.UnknownError }));
        });

        /// <summary>
        /// CallOptions にはセッションのキャンセルトークンを渡す。ResetSession でセッションを終わらせると、そのトークンがキャンセルされる。
        /// </summary>
        [UnityTest]
        [Description("CallOptions にセッションのキャンセルトークンを渡し、ResetSession でキャンセルする")]
        public IEnumerator PassesSessionCancellationToken() => UniTask.ToCoroutine(async () =>
        {
            await api.ReadAsync(1);
            var token = invoker.Calls[0].Options.CancellationToken;
            Assert.That(token.CanBeCanceled, Is.True);
            Assert.That(token.IsCancellationRequested, Is.False);

            core.ResetSession();
            await api.ReadAsync(2);

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(invoker.Calls[1].Options.CancellationToken.IsCancellationRequested, Is.False);
        });

        /// <summary>
        /// エラーハンドラーが Retry を返したら、同じ RequestID で送り直す。
        /// </summary>
        [UnityTest]
        [Description("エラーハンドラーが Retry を返したら、同じ RequestID で送り直す")]
        public IEnumerator RetriesWithSameRequestIdWhenErrorHandlerSaysRetry() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => invoker.Calls.Count == 1
                ? Failure(FakeRpcErrors.Status(StatusCode.Internal))
                : Success("ok");
            errorHandler.Decisions.Enqueue(ErrorDecision.Retry);

            var result = await api.WriteAsync("a");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(invoker.Calls.Count, Is.EqualTo(2));
            Assert.That(RequestIdRules.IsValid(invoker.Calls[0].Headers.GetValue(ApiHeaders.RequestId)), Is.True);
            Assert.That(
                invoker.Calls[1].Headers.GetValue(ApiHeaders.RequestId),
                Is.EqualTo(invoker.Calls[0].Headers.GetValue(ApiHeaders.RequestId)));
            Assert.That(errorHandler.Errors.Select(error => error.Code), Is.EqualTo(new[] { ReservedErrors.InternalError }));
        });

        /// <summary>
        /// ResetSession はトークンとゲーム内の日付を捨てる。次の呼び出しはトークンを取り直し、x-game-date を付けない。
        /// </summary>
        [UnityTest]
        [Description("ResetSession はトークンとゲーム内の日付を捨てる")]
        public IEnumerator ResetSessionDropsTokenAndGameDate() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Success("ok", new Metadata { { ApiHeaders.GameDate, "2026-10-30" } });
            await api.ReadAsync(1);
            await api.ReadAsync(2);

            core.ResetSession();
            await api.ReadAsync(3);

            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.GameDate), Is.EqualTo("2026-10-30"));
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-1"));
            Assert.That(invoker.Calls[2].Headers.GetValue(ApiHeaders.GameDate), Is.Null);
            Assert.That(invoker.Calls[2].Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-2"));
        });

        /// <summary>
        /// 業務エラー(FAILED_PRECONDITION + ErrorInfo)を handledCodes で受け取ると、エラーハンドラーを呼ばずに失敗として返す。
        /// </summary>
        [UnityTest]
        [Description("業務エラーを handledCodes で受け取ると、エラーハンドラーを呼ばずに失敗として返す")]
        public IEnumerator HandledBusinessErrorSkipsErrorHandler() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.FailedPrecondition(TestErrors.NotEnough, "not enough"));

            var result = await api.WriteAsync("a", new[] { TestErrors.NotEnough });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(TestErrors.NotEnough));
            Assert.That(result.Error.Action, Is.EqualTo(ErrorAction.Stay));
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 予約エラー(DATE_CHANGED)は handledCodes にあっても、エラーハンドラーに渡す。
        /// </summary>
        [UnityTest]
        [Description("予約エラーは handledCodes にあっても、エラーハンドラーに渡す")]
        public IEnumerator ReservedErrorGoesToErrorHandler() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.FailedPrecondition(ReservedErrors.DateChanged));

            var result = await api.WriteAsync("a", new[] { ReservedErrors.DateChanged });

            Assert.That(result.Error.Code, Is.EqualTo(ReservedErrors.DateChanged));
            Assert.That(errorHandler.Errors.Select(error => error.Code), Is.EqualTo(new[] { ReservedErrors.DateChanged }));
        });

        /// <summary>
        /// 知らない業務エラーは UNKNOWN_ERROR に、INTERNAL は INTERNAL_ERROR に変える。
        /// </summary>
        [UnityTest]
        [Description("知らない業務エラーは UNKNOWN_ERROR に、INTERNAL は INTERNAL_ERROR に変える")]
        public IEnumerator ConvertsUnknownAndInternalErrors() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(
                invoker.Calls.Count == 1
                    ? FakeRpcErrors.FailedPrecondition(new ErrorCode("test", "NEWER_ERROR"))
                    : FakeRpcErrors.Status(StatusCode.Internal));

            var unknown = await api.ReadAsync(1);
            var internalError = await api.ReadAsync(2);

            Assert.That(unknown.Error.Code, Is.EqualTo(ReservedErrors.UnknownError));
            Assert.That(internalError.Error.Code, Is.EqualTo(ReservedErrors.InternalError));
        });

        /// <summary>
        /// 共有インターフェースに無いメソッドの名前を渡すと、ArgumentException にする。
        /// </summary>
        [Test]
        [Description("共有インターフェースに無いメソッドの名前を渡すと、ArgumentException にする")]
        public void UnknownMethodNameThrows()
        {
            Assert.Throws<ArgumentException>(() => api.CallUnknown());
        }

        private static string MethodOf(FakeCall call)
        {
            return call.MethodFullName.Substring(call.MethodFullName.LastIndexOf('/') + 1);
        }

        private static Task<FakeResult> Success(string value, Metadata responseHeaders = null)
        {
            return Task.FromResult(FakeResult.Success(MessagePackSerializer.Serialize(value, TestClients.SerializerOptions), responseHeaders));
        }

        private static Task<FakeResult> Failure(Exception exception)
        {
            return Task.FromResult(FakeResult.Failure(exception));
        }

        // テスト用の業務エラー。static フィールドは持たない(UAL0010)
        private static class TestErrors
        {
            public static ErrorCode NotEnough => new ErrorCode("test", "NOT_ENOUGH");
        }

        // アプリが書くラッパーの見本
        private sealed class TestApi : ApiServiceBase<ITestService>
        {
            public TestApi(ApiCore core, ITestService client)
                : base(core, client)
            {
            }

            public UniTask<ApiResult<string>> WriteAsync(string value, IEnumerable<ErrorCode> handledCodes = null)
            {
                return InvokeAsync(nameof(ITestService.WriteAsync), value, static (client, request) => client.WriteAsync(request), handledCodes);
            }

            public UniTask<ApiResult<string>> ReadAsync(int id)
            {
                return InvokeAsync(nameof(ITestService.ReadAsync), id, static (client, request) => client.ReadAsync(request));
            }

            public UniTask<ApiResult<string>> PublicAsync(string value)
            {
                return InvokeAsync(nameof(ITestService.PublicAsync), value, static (client, request) => client.PublicAsync(request));
            }

            public UniTask<ApiResult<string>> CallUnknown()
            {
                return InvokeAsync("Unknown", 0, static (client, request) => client.ReadAsync(request));
            }
        }

        // 呼ばれるたびに token-1、token-2 と新しいトークンを返す
        private sealed class FakeTokenProvider : ITokenProvider
        {
            private int issued;
            private string current;

            public List<string> Invalidated { get; } = new();

            public UniTask<string> GetTokenAsync()
            {
                if (current == null)
                {
                    issued++;
                    current = $"token-{issued}";
                }

                return UniTask.FromResult(current);
            }

            public void Invalidate(string rejectedToken)
            {
                Invalidated.Add(rejectedToken);
                if (rejectedToken == current)
                {
                    current = null;
                }
            }

            public void Reset()
            {
                current = null;
            }
        }

        // ダイアログを出さずに、受け取ったエラーを記録して、Decisions に積んだ動きを順に返す。空なら Stay を返す
        private sealed class RecordingErrorHandler : ICommonErrorHandler
        {
            public List<ApiError> Errors { get; } = new();

            public Queue<ErrorDecision> Decisions { get; } = new();

            public UniTask<ErrorDecision> HandleAsync(ApiError error)
            {
                Errors.Add(error);
                return UniTask.FromResult(Decisions.Count > 0 ? Decisions.Dequeue() : ErrorDecision.Stay);
            }
        }
    }

    /// <summary>
    /// ApiCoreTests で使う共有インターフェース。
    /// </summary>
    public interface ITestService : IService<ITestService>
    {
        /// <summary>冪等性の対象の RPC。</summary>
        UnaryResult<string> WriteAsync(string value);

        /// <summary>副作用の無い RPC。</summary>
        [NoSideEffects]
        UnaryResult<string> ReadAsync(int id);

        /// <summary>認証の要らない RPC。冪等性の対象にもならない。</summary>
        [SkipAuth]
        UnaryResult<string> PublicAsync(string value);

        /// <summary>値型を返す RPC。</summary>
        [NoSideEffects]
        UnaryResult<int> CountAsync();
    }

    /// <summary>
    /// ITestService と別のサービス。サービスをまたいだ順番待ちの確認に使う。
    /// </summary>
    public interface IOtherTestService : IService<IOtherTestService>
    {
        /// <summary>冪等性の対象の RPC。</summary>
        UnaryResult<string> WriteAsync(string value);
    }
}
