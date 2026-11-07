using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using MagicOnion.Client;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Client;
using VolTech.Client.Auth;
using VolTech.Client.Unity;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.E2E
{
    /// <summary>
    /// Unity のクライアントが本物のサーバーと通信できることを確かめる。tests/VolTech.E2E.Server を起動してから動かす。
    /// </summary>
    /// <remarks>
    /// YetAnotherHttpHandler、本物の DeviceTokenProvider、ApiCore を通す。サーバーが動いていなければ失敗する。
    /// </remarks>
    [Description("Unity のクライアントが本物のサーバーと通信できることを確かめる")]
    public class E2ETests
    {
        private const int AppVersionNumber = 42;

        private GrpcChannel channel;
        private RecordingErrorHandler errorHandler;
        private RecordingApplier applier;
        private ApiCore core;
        private IE2EService client;

        private static string Address => Environment.GetEnvironmentVariable("VOLTECH_E2E_ADDRESS") ?? "http://localhost:5199";

        private static string NewKey => Guid.NewGuid().ToString("N");

        /// <summary>
        /// テストごとに、別の端末としてチャンネルと ApiCore を作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            channel = ApiChannel.Create(Address);
            var callInvoker = ApiChannel.CreateCallInvoker(channel);
            var options = new ApiCoreOptions(new AppVersion(AppVersion.Windows, AppVersionNumber));
            var deviceClient = MagicOnionClient.Create<IDeviceService>(
                callInvoker,
                E2EClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                E2EClients.ClientFactoryProvider);
            var tokenProvider = new DeviceTokenProvider(deviceClient, new InMemoryAccountStore(), options);
            errorHandler = new RecordingErrorHandler();
            applier = new RecordingApplier();
            core = ApiCore.CreateBuilder(
                    callInvoker,
                    E2EClients.SerializerProvider,
                    E2EClients.ClientFactoryProvider,
                    options,
                    tokenProvider,
                    new ErrorPolicyProvider(new[] { new ErrorDefinition(E2EErrors.NotEnough, ErrorAction.Stay) }),
                    errorHandler)
                .WithCommonResponseApplier(applier)
                .Build();
            client = core.CreateClient<IE2EService>();
        }

        /// <summary>
        /// テストごとにチャンネルを閉じる。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            channel.Dispose();
        }

        /// <summary>
        /// 端末登録とトークンの発行を通り、ヘッダーがサーバーに届く。
        /// </summary>
        [UnityTest]
        [Description("端末登録とトークンの発行を通り、ヘッダーがサーバーに届く")]
        public IEnumerator HeadersReachServer() => UniTask.ToCoroutine(async () =>
        {
            var result = await client.EchoAsync(NewKey).ToResult();

            Assert.That(result.IsError, Is.False, result.ErrorMessage);
            Assert.That(result.Value.PlayerId, Is.Not.Empty);
            Assert.That(RequestIdRules.IsValid(result.Value.RequestId), Is.True);
            Assert.That(result.Value.AppVersion, Is.EqualTo(AppVersionNumber.ToString()));
        });

        /// <summary>
        /// 最初の応答で受け取った x-game-date を、次のリクエストでサーバーに送る。
        /// </summary>
        [UnityTest]
        [Description("最初の応答で受け取った x-game-date を、次のリクエストでサーバーに送る")]
        public IEnumerator GameDateIsSentBack() => UniTask.ToCoroutine(async () =>
        {
            var first = await client.ReadAsync(NewKey).ToResult();
            var second = await client.ReadAsync(NewKey).ToResult();

            Assert.That(first.Value.GameDate, Is.Empty);
            Assert.That(second.Value.GameDate, Is.Not.Empty);
            Assert.That(core.ServerClock.Now.Year, Is.GreaterThan(2000));
        });

        /// <summary>
        /// [SkipAuth] の RPC は、トークンも x-request-id も付けずに呼べる。
        /// </summary>
        [UnityTest]
        [Description("[SkipAuth] の RPC は、トークンも x-request-id も付けずに呼べる")]
        public IEnumerator SkipAuthRpcWorks() => UniTask.ToCoroutine(async () =>
        {
            var result = await client.PublicAsync(NewKey).ToResult();

            Assert.That(result.IsError, Is.False, result.ErrorMessage);
            Assert.That(result.Value.PlayerId, Is.Empty);
            Assert.That(result.Value.RequestId, Is.Empty);
        });

        /// <summary>
        /// サーバーが詰めた Common が、呼び出し側に返る前に反映処理に届く。
        /// </summary>
        [UnityTest]
        [Description("サーバーが詰めた Common が、呼び出し側に返る前に反映処理に届く")]
        public IEnumerator CommonReachesApplier() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey;

            var result = await client.EchoAsync(key).ToResult();

            Assert.That(result.IsError, Is.False, result.ErrorMessage);
            Assert.That(applier.Notes, Is.EqualTo(new[] { "echo:" + key }));
        });

        /// <summary>
        /// 応答を失って Retry で送り直すと、同じ x-request-id で届き、本体を実行せずに保存済みのレスポンスが返る。
        /// </summary>
        [UnityTest]
        [Description("応答を失って Retry で送り直すと、本体を実行せずに保存済みのレスポンスが返る")]
        public IEnumerator RetryAfterLostResponseDoesNotRunTwice() => UniTask.ToCoroutine(async () =>
        {
            errorHandler.Decisions.Enqueue(ErrorDecision.Retry);

            var result = await client.LoseResponseOnceAsync(NewKey).ToResult();

            Assert.That(result.IsError, Is.False, result.ErrorMessage);
            Assert.That(result.Value.BodyRuns, Is.EqualTo(1));
            Assert.That(errorHandler.Errors.Count, Is.EqualTo(1));
        });

        /// <summary>
        /// サーバーでトークンが無効になっていたら、取り直して 1 回だけ送り直す。
        /// </summary>
        [UnityTest]
        [Description("サーバーでトークンが無効になっていたら、取り直して 1 回だけ送り直す")]
        public IEnumerator ReauthenticatesWhenTokenIsRejected() => UniTask.ToCoroutine(async () =>
        {
            var expired = await client.ExpireTokenAsync().ToResult();
            Assert.That(expired.IsError, Is.False, expired.ErrorMessage);

            var result = await client.EchoAsync(NewKey).ToResult();

            Assert.That(result.IsError, Is.False, result.ErrorMessage);
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 業務エラーの ErrorInfo が grpc-status-details-bin で届く。指定したコードは共通エラーハンドラーを通らない。
        /// </summary>
        [UnityTest]
        [Description("業務エラーの ErrorInfo が届く。指定したコードは共通エラーハンドラーを通らない")]
        public IEnumerator BusinessErrorReachesCaller() => UniTask.ToCoroutine(async () =>
        {
            var result = await client.WithHandledCodes(E2EErrors.NotEnough).ThrowBusinessAsync().ToResult();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo(E2EErrors.NotEnough));
            Assert.That(result.Error.StatusCode, Is.EqualTo(StatusCode.FailedPrecondition));
            Assert.That(result.Error.Metadata[E2EErrors.MetadataKey], Is.EqualTo("300"));
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 指定していない業務エラーは、共通エラーハンドラーに渡る。
        /// </summary>
        [UnityTest]
        [Description("指定していない業務エラーは、共通エラーハンドラーに渡る")]
        public IEnumerator UnhandledBusinessErrorGoesToHandler() => UniTask.ToCoroutine(async () =>
        {
            var result = await client.ThrowBusinessAsync().ToResult();

            Assert.That(result.ErrorCode, Is.EqualTo(E2EErrors.NotEnough));
            Assert.That(errorHandler.Errors.Count, Is.EqualTo(1));
        });

        /// <summary>
        /// 予期しない例外は INTERNAL_ERROR で届き、内容はクライアントに漏れない。
        /// </summary>
        [UnityTest]
        [Description("予期しない例外は INTERNAL_ERROR で届き、内容はクライアントに漏れない")]
        public IEnumerator UnexpectedErrorIsInternal() => UniTask.ToCoroutine(async () =>
        {
            var result = await client.ThrowUnexpectedAsync().ToResult();

            Assert.That(result.ErrorCode, Is.EqualTo(ReservedErrors.InternalError));
            Assert.That(result.ErrorMessage, Does.Not.Contain("secret"));
        });

        /// <summary>
        /// RunAsync を通らないレスポンスは、検査で INTERNAL_ERROR になる。
        /// </summary>
        [UnityTest]
        [Description("RunAsync を通らないレスポンスは、検査で INTERNAL_ERROR になる")]
        public IEnumerator ResponseWithoutRunAsyncIsInternal() => UniTask.ToCoroutine(async () =>
        {
            var result = await client.WithoutRunAsync().ToResult();

            Assert.That(result.ErrorCode, Is.EqualTo(ReservedErrors.InternalError));
        });

        /// <summary>
        /// ResetSession で通信が止まり、サーバーの処理もキャンセルされる。
        /// </summary>
        [UnityTest]
        [Description("ResetSession で通信が止まり、サーバーの処理もキャンセルされる")]
        public IEnumerator ResetSessionStopsTheCall() => UniTask.ToCoroutine(async () =>
        {
            var cancelledBefore = (await client.CancelledSlowCallsAsync().ToResult()).Value;

            var slow = client.SlowAsync(30000).ToResult();
            await UniTask.Delay(500);
            core.ResetSession();
            var result = await slow;

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Error.IsDiscarded, Is.True);

            var cancelledAfter = cancelledBefore;
            for (var i = 0; i < 50 && cancelledAfter == cancelledBefore; i++)
            {
                await UniTask.Delay(100);
                cancelledAfter = (await client.CancelledSlowCallsAsync().ToResult()).Value;
            }

            Assert.That(cancelledAfter, Is.EqualTo(cancelledBefore + 1));
        });

        /// <summary>
        /// 締め切りを過ぎると通信が止まる。ApiCoreOptions.Deadline は 15 秒。
        /// </summary>
        [UnityTest]
        [Description("締め切りを過ぎると通信が止まる")]
        public IEnumerator DeadlineStopsTheCall() => UniTask.ToCoroutine(async () =>
        {
            var result = await client.SlowAsync(30000).ToResult();

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Error.StatusCode, Is.EqualTo(StatusCode.DeadlineExceeded));
            Assert.That(errorHandler.Errors.Count, Is.EqualTo(1));
        });

        // 受け取ったエラーを記録して、Decisions に積んだ動きを順に返す。空なら Stay を返す
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

        private sealed class RecordingApplier : ICommonResponseApplier<E2ECommon>
        {
            public List<string> Notes { get; } = new();

            public UniTask ApplyAsync(E2ECommon common)
            {
                Notes.Add(common.Note);
                return UniTask.CompletedTask;
            }
        }
    }
}
