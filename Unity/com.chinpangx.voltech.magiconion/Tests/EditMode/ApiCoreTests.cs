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
using VolTech.Client.Auth;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// ApiCore が、MagicOnion のクライアントで、ヘッダー・RPC ごとの設定・再認証・エラーの変換を仕様どおりに行うことを確かめる。
    /// </summary>
    [Description("ApiCore が、MagicOnion のクライアントでヘッダー・RPC ごとの設定・再認証・エラーの変換を仕様どおりに行うことを確かめる")]
    public class ApiCoreTests
    {
        private FakeCallInvoker invoker;
        private FakeTokenProvider tokenProvider;
        private RecordingErrorHandler errorHandler;
        private ServerClock serverClock;
        private ApiCore core;
        private ITestService client;

        /// <summary>
        /// テストごとに、成功を返す偽の CallInvoker と、それを呼ぶ ApiCore を作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            invoker = new FakeCallInvoker { Handler = call => Success(MethodOf(call) + ":ok") };
            tokenProvider = new FakeTokenProvider();
            errorHandler = new RecordingErrorHandler();
            serverClock = new ServerClock(() => TimeSpan.Zero);
            core = CreateBuilder(new ApiCoreOptions()).WithServerClock(serverClock).Build();
            client = core.CreateClient<ITestService>();
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
            var write = await client.WriteAsync("a").ToResult();
            var read = await client.ReadAsync(1).ToResult();

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

            await client.ReadAsync(1).ToResult();
            await client.ReadAsync(2).ToResult();
            await client.ReadAsync(3).ToResult();

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

            var result = await client.WriteAsync("a").ToResult();

            Assert.That(result.IsError, Is.False);
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

            var result = await client.WriteAsync("a").ToResult();

            Assert.That(result.IsError, Is.True);
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

            var result = await client.WriteAsync("a").ToResult();

            Assert.That(result.IsError, Is.False);
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
            var result = await client.PublicAsync("a").ToResult();

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

            var result = await client.WriteAsync("a").ToResult();

            Assert.That(result.IsError, Is.False);
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

            var write = client.WriteAsync("a").ToResult();
            var publicCall = client.PublicAsync("b").ToResult();

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

            var result = await client.PublicAsync("a").ToResult();

            Assert.That(result.IsError, Is.True);
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
            await client.ReadAsync(1).ToResult();
            var token = invoker.Calls[0].Options.CancellationToken;
            Assert.That(token.CanBeCanceled, Is.True);
            Assert.That(token.IsCancellationRequested, Is.False);

            core.ResetSession();
            await client.ReadAsync(2).ToResult();

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

            var result = await client.WriteAsync("a").ToResult();

            Assert.That(result.IsError, Is.False);
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
            await client.ReadAsync(1).ToResult();
            await client.ReadAsync(2).ToResult();

            core.ResetSession();
            await client.ReadAsync(3).ToResult();

            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.GameDate), Is.EqualTo("2026-10-30"));
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-1"));
            Assert.That(invoker.Calls[2].Headers.GetValue(ApiHeaders.GameDate), Is.Null);
            Assert.That(invoker.Calls[2].Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-2"));
        });

        /// <summary>
        /// 業務エラー(FAILED_PRECONDITION + ErrorInfo)を WithHandledCodes で指定すると、エラーハンドラーを呼ばずに失敗として返す。
        /// </summary>
        [UnityTest]
        [Description("業務エラーを WithHandledCodes で指定すると、エラーハンドラーを呼ばずに失敗として返す")]
        public IEnumerator HandledBusinessErrorSkipsErrorHandler() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.FailedPrecondition(TestErrors.NotEnough, "not enough"));

            var result = await client.WithHandledCodes(TestErrors.NotEnough).WriteAsync("a").ToResult();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo(TestErrors.NotEnough));
            Assert.That(result.Error.Action, Is.EqualTo(ErrorAction.Stay));
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 予約エラー(DATE_CHANGED)は WithHandledCodes で指定しても、エラーハンドラーに渡す。
        /// </summary>
        [UnityTest]
        [Description("予約エラーは WithHandledCodes で指定しても、エラーハンドラーに渡す")]
        public IEnumerator ReservedErrorGoesToErrorHandler() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.FailedPrecondition(ReservedErrors.DateChanged));

            var result = await client.WithHandledCodes(ReservedErrors.DateChanged).WriteAsync("a").ToResult();

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

            var unknown = await client.ReadAsync(1).ToResult();
            var internalError = await client.ReadAsync(2).ToResult();

            Assert.That(unknown.Error.Code, Is.EqualTo(ReservedErrors.UnknownError));
            Assert.That(internalError.Error.Code, Is.EqualTo(ReservedErrors.InternalError));
        });

        /// <summary>
        /// アプリのバージョンのヘッダーは、ApiCoreOptions.AppVersion があるときだけ付ける。
        /// </summary>
        [UnityTest]
        [Description("アプリのバージョンのヘッダーは、ApiCoreOptions.AppVersion があるときだけ付ける")]
        public IEnumerator SendsAppVersion() => UniTask.ToCoroutine(async () =>
        {
            var withVersion = CreateBuilder(new ApiCoreOptions(new AppVersion(AppVersion.Android, 42))).Build().CreateClient<ITestService>();

            await client.ReadAsync(1).ToResult();
            await withVersion.ReadAsync(1).ToResult();

            Assert.That(invoker.Calls[0].Headers.GetValue(ApiHeaders.AppPlatform), Is.Null);
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.AppPlatform), Is.EqualTo("android"));
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.AppVersion), Is.EqualTo("42"));
        });

        /// <summary>
        /// 別のサービスのクライアントどうしでも、冪等性の対象の RPC は順番に送る。
        /// </summary>
        [UnityTest]
        [Description("別のサービスのクライアントどうしでも、冪等性の対象の RPC は順番に送る")]
        public IEnumerator IdempotentRpcsWaitAcrossServices() => UniTask.ToCoroutine(async () =>
        {
            var other = core.CreateClient<IOtherTestService>();
            var pendingWrite = new TaskCompletionSource<FakeResult>();
            invoker.Handler = call => call.Index == 0 ? pendingWrite.Task : Success("other");

            var first = client.WriteAsync("a").ToResult();
            var second = other.WriteAsync("b").ToResult();
            await UniTask.Yield();

            Assert.That(invoker.Calls.Count, Is.EqualTo(1));

            pendingWrite.SetResult(await Success("written"));
            await first;
            await second;

            Assert.That(invoker.Calls.Count, Is.EqualTo(2));
        });

        /// <summary>
        /// 同じクライアントの同時の呼び出しで、ヘッダーが混ざらない。
        /// </summary>
        [UnityTest]
        [Description("同じクライアントの同時の呼び出しで、ヘッダーが混ざらない")]
        public IEnumerator ConcurrentCallsDoNotShareHeaders() => UniTask.ToCoroutine(async () =>
        {
            var pendingWrite = new TaskCompletionSource<FakeResult>();
            invoker.Handler = call => MethodOf(call) == nameof(ITestService.WriteAsync) ? pendingWrite.Task : Success("ok");

            var write = client.WriteAsync("a").ToResult();
            await client.PublicAsync("b").ToResult();
            pendingWrite.SetResult(await Success("written"));
            await write;

            Assert.That(invoker.Calls[0].Headers.GetValue(ApiHeaders.RequestId), Is.Not.Null);
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.RequestId), Is.Null);
            Assert.That(invoker.Calls[1].Headers.GetValue(ApiHeaders.Authorization), Is.Null);
        });

        /// <summary>
        /// 内部のヘッダーと、アプリが WithHeaders で付けたヘッダーは送らない。
        /// </summary>
        [UnityTest]
        [Description("内部のヘッダーと、アプリが WithHeaders で付けたヘッダーは送らない")]
        public IEnumerator SendsOnlyFoundationHeaders() => UniTask.ToCoroutine(async () =>
        {
            await client.WithHandledCodes(TestErrors.NotEnough).ReadAsync(1).ToResult();
            await client.WithHeaders(new Metadata { { "x-app-header", "1" } }).ReadAsync(2).ToResult();

            Assert.That(invoker.Calls[0].Headers.Select(entry => entry.Key), Is.EqualTo(new[] { ApiHeaders.Authorization }));
            Assert.That(invoker.Calls[1].Headers.Select(entry => entry.Key), Is.EqualTo(new[] { ApiHeaders.Authorization }));
        });

        /// <summary>
        /// WithHandledCodes のクライアントを変数に持って何度呼んでも、指定が効く。
        /// </summary>
        [UnityTest]
        [Description("WithHandledCodes のクライアントを変数に持って何度呼んでも、指定が効く")]
        public IEnumerator HandledCodesWorkOnHeldClient() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.FailedPrecondition(TestErrors.NotEnough, "not enough"));
            var held = client.WithHandledCodes(TestErrors.NotEnough);

            await held.ReadAsync(1).ToResult();
            var second = await held.ReadAsync(2).ToResult();

            Assert.That(second.IsError, Is.True);
            Assert.That(second.ErrorCode, Is.EqualTo(TestErrors.NotEnough));
            Assert.That(second.ErrorMessage, Is.Not.Null);
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 締め切りは試行ごとに計算する。
        /// </summary>
        [UnityTest]
        [Description("締め切りは試行ごとに計算する")]
        public IEnumerator DeadlineIsSetForEachAttempt() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = async call =>
            {
                if (call.Index != 0)
                {
                    return await Success("ok");
                }

                // 同じ締め切りを使い回していたら見分けられるように、時間を置いてから失敗させる
                await Task.Delay(50);
                return await Failure(FakeRpcErrors.Status(StatusCode.Unauthenticated));
            };

            await client.WriteAsync("a").ToResult();

            Assert.That(invoker.Calls[1].Options.Deadline, Is.GreaterThan(invoker.Calls[0].Options.Deadline));
        });

        /// <summary>
        /// トークンの取得の間にセッションを終わらせると、通信に進まずに捨てる。
        /// </summary>
        [UnityTest]
        [Description("トークンの取得の間にセッションを終わらせると、通信に進まずに捨てる")]
        public IEnumerator ResetWhileGettingTokenDiscardsTheCall() => UniTask.ToCoroutine(async () =>
        {
            var handled = 0;
            invoker.Handler = call =>
            {
                handled++;
                return Success("ok");
            };
            tokenProvider.OnGetToken = () =>
            {
                tokenProvider.OnGetToken = null;
                core.ResetSession();
            };

            var result = await client.WriteAsync("a").ToResult();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Error.IsDiscarded, Is.True);
            Assert.That(handled, Is.Zero);
        });

        /// <summary>
        /// 順番待ちの間にセッションを終わらせると、送らずに捨てる。
        /// </summary>
        [UnityTest]
        [Description("順番待ちの間にセッションを終わらせると、送らずに捨てる")]
        public IEnumerator ResetWhileWaitingInQueueDiscardsTheCall() => UniTask.ToCoroutine(async () =>
        {
            var pendingWrite = new TaskCompletionSource<FakeResult>();
            invoker.Handler = call => pendingWrite.Task;

            var first = client.WriteAsync("a").ToResult();
            var second = client.WriteAsync("b").ToResult();
            core.ResetSession();
            var secondResult = await second;
            pendingWrite.SetResult(await Success("written"));
            await first;

            Assert.That(secondResult.Error.IsDiscarded, Is.True);
            Assert.That(invoker.Calls.Count, Is.EqualTo(1));
        });

        /// <summary>
        /// ToResult を付けないと、共通エラーハンドラーの処理の後で ApiException が届く。
        /// </summary>
        [UnityTest]
        [Description("ToResult を付けないと、共通エラーハンドラーの処理の後で ApiException が届く")]
        public IEnumerator ApiExceptionReachesCallerWithoutToResult() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.Status(StatusCode.Internal));

            ApiException caught = null;
            try
            {
                await client.ReadAsync(1);
            }
            catch (ApiException exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught.Error.Code, Is.EqualTo(ReservedErrors.InternalError));
            Assert.That(errorHandler.Errors.Count, Is.EqualTo(1));
        });

        /// <summary>
        /// WithoutActivity は進行中を知らせない。指定が無ければ知らせる。
        /// </summary>
        [UnityTest]
        [Description("WithoutActivity は進行中を知らせない。指定が無ければ知らせる")]
        public IEnumerator WithoutActivityDoesNotNotify() => UniTask.ToCoroutine(async () =>
        {
            var activity = new RecordingActivityHandler();
            var notifying = CreateBuilder(new ApiCoreOptions()).AddActivityHandler(activity).Build().CreateClient<ITestService>();

            await notifying.WithoutActivity().ReadAsync(1).ToResult();
            Assert.That(activity.Started, Is.Zero);

            await notifying.ReadAsync(2).ToResult();
            Assert.That(activity.Started, Is.EqualTo(1));
            Assert.That(activity.Ended, Is.EqualTo(1));
        });

        /// <summary>
        /// WithoutActivity にコードを渡すと、WithHandledCodes と同じく共通エラーハンドラーを通さない。
        /// </summary>
        [UnityTest]
        [Description("WithoutActivity にコードを渡すと、共通エラーハンドラーを通さない")]
        public IEnumerator WithoutActivityCanAlsoHandleCodes() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.FailedPrecondition(TestErrors.NotEnough, "not enough"));

            var result = await client.WithoutActivity(TestErrors.NotEnough).ReadAsync(1).ToResult();

            Assert.That(result.ErrorCode, Is.EqualTo(TestErrors.NotEnough));
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 端末のアカウント情報の読み書きの失敗は、共通エラーハンドラーを通さず、呼び出し側に返す。
        /// </summary>
        [UnityTest]
        [Description("端末のアカウント情報の読み書きの失敗は、共通エラーハンドラーを通さず、呼び出し側に返す")]
        public IEnumerator AccountErrorsGoToCaller() => UniTask.ToCoroutine(async () =>
        {
            tokenProvider.OnGetToken = () => throw new CorruptedAccountDataException("account", new Exception("broken"));
            var corrupted = await client.ReadAsync(1).ToResult();

            tokenProvider.OnGetToken = () => throw new AccountSaveFailedException(new Exception("full"));
            var saveFailed = await client.ReadAsync(1).ToResult();

            Assert.That(corrupted.ErrorCode, Is.EqualTo(ReservedErrors.AccountDataCorrupted));
            Assert.That(saveFailed.ErrorCode, Is.EqualTo(ReservedErrors.AccountSaveFailed));
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// トークンを取るときに UNAUTHENTICATED を受けたら、何も捨てず、送り直さずに UNKNOWN_ERROR にする。
        /// </summary>
        [UnityTest]
        [Description("トークンを取るときに UNAUTHENTICATED を受けたら、何も捨てず、送り直さずに UNKNOWN_ERROR にする")]
        public IEnumerator UnauthenticatedWhileGettingTokenIsNotResent() => UniTask.ToCoroutine(async () =>
        {
            tokenProvider.OnGetToken = () => throw FakeRpcErrors.Status(StatusCode.Unauthenticated);

            var result = await client.ReadAsync(1).ToResult();

            Assert.That(result.ErrorCode, Is.EqualTo(ReservedErrors.UnknownError));
            Assert.That(tokenProvider.Invalidated, Is.Empty);
            Assert.That(invoker.Calls, Is.Empty);
            Assert.That(errorHandler.Errors.Count, Is.EqualTo(1));
        });

        /// <summary>
        /// x-voltech-call を読めなければ、基盤の不具合として例外にする。
        /// </summary>
        [UnityTest]
        [Description("x-voltech-call を読めなければ、基盤の不具合として例外にする")]
        public IEnumerator BrokenCallHeaderThrows() => UniTask.ToCoroutine(async () =>
        {
            Exception caught = null;
            try
            {
                await client.WithHeaders(new Metadata { { "x-voltech-call", "broken" } }).ReadAsync(1).ToResult();
            }
            catch (InvalidOperationException exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(invoker.Calls, Is.Empty);
        });

        /// <summary>
        /// エラーコードに区切りの文字が入っていても、WithHandledCodes の指定が効く。
        /// </summary>
        [UnityTest]
        [Description("エラーコードに区切りの文字が入っていても、WithHandledCodes の指定が効く")]
        public IEnumerator HandledCodesAreEscaped() => UniTask.ToCoroutine(async () =>
        {
            invoker.Handler = call => Failure(FakeRpcErrors.FailedPrecondition(TestErrors.WithSeparators, "failed"));

            var result = await client.WithHandledCodes(TestErrors.WithSeparators).ReadAsync(1).ToResult();

            Assert.That(result.IsError, Is.True);
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// FakeRpcErrors.ApiFailureResult で作った失敗は、ToResult が失敗の結果にする。
        /// </summary>
        [UnityTest]
        [Description("FakeRpcErrors.ApiFailureResult で作った失敗は、ToResult が失敗の結果にする")]
        public IEnumerator ApiFailureResultBecomesFailedResult() => UniTask.ToCoroutine(async () =>
        {
            var result = await FakeRpcErrors.ApiFailureResult<string>(TestErrors.NotEnough, "not enough").ToResult();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo(TestErrors.NotEnough));
            Assert.That(result.ErrorMessage, Is.EqualTo("not enough"));
        });

        /// <summary>
        /// 値型のレスポンスの RPC は、反映処理があっても動く。
        /// </summary>
        [UnityTest]
        [Description("値型のレスポンスの RPC は、反映処理があっても動く")]
        public IEnumerator ValueTypeResponseWorksWithApplier() => UniTask.ToCoroutine(async () =>
        {
            var applier = new RecordingResponseApplier();
            var withApplier = CreateBuilder(new ApiCoreOptions()).WithCommonResponseApplier(applier).Build().CreateClient<ITestService>();
            invoker.Handler = call => Task.FromResult(FakeResult.Success(MessagePackSerializer.Serialize(5, TestClients.SerializerOptions)));

            var result = await withApplier.CountAsync().ToResult();

            Assert.That(result.Value, Is.EqualTo(5));
            Assert.That(applier.Called, Is.False);
        });

        private ApiCoreBuilder CreateBuilder(ApiCoreOptions options)
        {
            return ApiCore.CreateBuilder(
                invoker,
                TestClients.SerializerProvider,
                TestClients.ClientFactoryProvider,
                options,
                tokenProvider,
                new ErrorPolicyProvider(new[]
                {
                    new ErrorDefinition(TestErrors.NotEnough, ErrorAction.Stay),
                    new ErrorDefinition(TestErrors.WithSeparators, ErrorAction.Stay),
                }),
                errorHandler);
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

            public static ErrorCode WithSeparators => new ErrorCode("test/a,b", "NOT;ENOUGH");
        }

        private sealed class RecordingActivityHandler : IApiActivityHandler
        {
            public int Started { get; private set; }

            public int Ended { get; private set; }

            public void OnActivityStarted()
            {
                Started++;
            }

            public void OnActivityEnded()
            {
                Ended++;
            }
        }

        private sealed class RecordingResponseApplier : ICommonResponseApplier<string>
        {
            public bool Called { get; private set; }

            public UniTask ApplyAsync(string common)
            {
                Called = true;
                return UniTask.CompletedTask;
            }
        }

        // 呼ばれるたびに token-1、token-2 と新しいトークンを返す
        private sealed class FakeTokenProvider : ITokenProvider
        {
            private int issued;
            private string current;

            public List<string> Invalidated { get; } = new();

            // トークンの取得の途中で動かす処理
            public Action OnGetToken { get; set; }

            public UniTask<string> GetTokenAsync()
            {
                OnGetToken?.Invoke();
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
