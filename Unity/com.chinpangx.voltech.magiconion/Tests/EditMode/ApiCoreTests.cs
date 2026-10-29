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
        private FakeTestService service;
        private FakeTokenProvider tokenProvider;
        private RecordingErrorHandler errorHandler;
        private ServerClock serverClock;
        private TestApi api;

        /// <summary>
        /// テストごとに、成功を返す偽のサービスと、それを呼ぶ ApiCore を作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            service = new FakeTestService();
            service.Handler = (method, _) => FakeResponse.Success(method + ":ok");
            tokenProvider = new FakeTokenProvider();
            errorHandler = new RecordingErrorHandler();
            serverClock = new ServerClock(() => TimeSpan.Zero);
            var core = ApiCore.CreateBuilder(
                    new ApiCoreOptions(),
                    tokenProvider,
                    new ErrorPolicyProvider(new[] { new ErrorDefinition(TestErrors.NotEnough, ErrorAction.Stay) }),
                    errorHandler)
                .WithServerClock(serverClock)
                .Build();
            api = new TestApi(core, service);
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
            Assert.That(device[nameof(IDeviceService.RegisterAsync)].MethodName, Is.EqualTo("IDeviceService/RegisterAsync"));
            Assert.That(test[nameof(ITestService.ReadAsync)].IsIdempotencyTarget, Is.False);
            Assert.That(test[nameof(ITestService.WriteAsync)].IsIdempotencyTarget, Is.True);
            Assert.That(test.Values.Select(settings => settings.RequiresAuth), Is.All.True);
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
            var writeCall = service.Calls[0];
            var readCall = service.Calls[1];
            Assert.That(writeCall.Options.Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-1"));
            Assert.That(RequestIdRules.IsValid(writeCall.Options.Headers.GetValue(ApiHeaders.RequestId)), Is.True);
            Assert.That(readCall.Options.Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-1"));
            Assert.That(readCall.Options.Headers.GetValue(ApiHeaders.RequestId), Is.Null);
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
            service.Handler = (method, _) => FakeResponse.Success(
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
            Assert.That(service.Calls[0].Options.Headers.GetValue(ApiHeaders.GameDate), Is.Null);
            Assert.That(service.Calls[1].Options.Headers.GetValue(ApiHeaders.GameDate), Is.EqualTo("2026-10-30"));
            Assert.That(service.Calls[2].Options.Headers.GetValue(ApiHeaders.GameDate), Is.EqualTo("2026-10-30"));
        });

        /// <summary>
        /// UNAUTHENTICATED なら、トークンを取り直して同じ RequestID で1回だけ送り直す。
        /// </summary>
        [UnityTest]
        [Description("UNAUTHENTICATED なら、トークンを取り直して同じ RequestID で1回だけ送り直す")]
        public IEnumerator ResendsOnceAfterUnauthenticated() => UniTask.ToCoroutine(async () =>
        {
            service.Handler = (method, _) => service.Calls.Count == 1
                ? FakeResponse.Failure<string>(FakeRpcErrors.Status(StatusCode.Unauthenticated))
                : FakeResponse.Success("ok");

            var result = await api.WriteAsync("a");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(tokenProvider.Invalidated, Is.EqualTo(new[] { "token-1" }));
            Assert.That(service.Calls.Count, Is.EqualTo(2));
            Assert.That(service.Calls[1].Options.Headers.GetValue(ApiHeaders.Authorization), Is.EqualTo("Bearer token-2"));
            Assert.That(
                service.Calls[1].Options.Headers.GetValue(ApiHeaders.RequestId),
                Is.EqualTo(service.Calls[0].Options.Headers.GetValue(ApiHeaders.RequestId)));
            Assert.That(errorHandler.Errors, Is.Empty);
        });

        /// <summary>
        /// 業務エラー(FAILED_PRECONDITION + ErrorInfo)を handledCodes で受け取ると、エラーハンドラーを呼ばずに失敗として返す。
        /// </summary>
        [UnityTest]
        [Description("業務エラーを handledCodes で受け取ると、エラーハンドラーを呼ばずに失敗として返す")]
        public IEnumerator HandledBusinessErrorSkipsErrorHandler() => UniTask.ToCoroutine(async () =>
        {
            service.Handler = (method, _) => FakeResponse.Failure<string>(FakeRpcErrors.FailedPrecondition(TestErrors.NotEnough, "not enough"));

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
            service.Handler = (method, _) => FakeResponse.Failure<string>(FakeRpcErrors.FailedPrecondition(ReservedErrors.DateChanged));

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
            service.Handler = (method, _) => FakeResponse.Failure<string>(
                service.Calls.Count == 1
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

        // テスト用の業務エラー。static フィールドは持たない(UAL0010)
        private static class TestErrors
        {
            public static ErrorCode NotEnough => new ErrorCode("test", "NOT_ENOUGH");
        }

        // FakeTestService に届いた1回の呼び出し
        private sealed class TestCall
        {
            public TestCall(string method, CallOptions options)
            {
                Method = method;
                Options = options;
            }

            public string Method { get; }

            public CallOptions Options { get; }
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

            public UniTask<ApiResult<string>> CallUnknown()
            {
                return InvokeAsync("Unknown", 0, static (client, request) => client.ReadAsync(request));
            }
        }

        // 通信する代わりに Handler を呼ぶ ITestService。WithOptions で渡されたヘッダーと締め切りを呼び出しと一緒に記録する
        private sealed class FakeTestService : ITestService
        {
            private readonly FakeTestService root;
            private readonly CallOptions options;

            public FakeTestService()
            {
                root = this;
            }

            private FakeTestService(FakeTestService root, CallOptions options)
            {
                this.root = root;
                this.options = options;
            }

            public List<TestCall> Calls { get; } = new();

            public Func<string, object, UnaryResult<string>> Handler { get; set; }

            public UnaryResult<string> WriteAsync(string value)
            {
                return Invoke(nameof(WriteAsync), value);
            }

            public UnaryResult<string> ReadAsync(int id)
            {
                return Invoke(nameof(ReadAsync), id);
            }

            public ITestService WithOptions(CallOptions option)
            {
                return new FakeTestService(root, option);
            }

            public ITestService WithHeaders(Metadata headers)
            {
                return new FakeTestService(root, options.WithHeaders(headers));
            }

            public ITestService WithDeadline(DateTime deadline)
            {
                return new FakeTestService(root, options.WithDeadline(deadline));
            }

            public ITestService WithCancellationToken(CancellationToken cancellationToken)
            {
                return new FakeTestService(root, options.WithCancellationToken(cancellationToken));
            }

            public ITestService WithHost(string host)
            {
                throw new NotSupportedException();
            }

            private UnaryResult<string> Invoke(string method, object request)
            {
                root.Calls.Add(new TestCall(method, options));
                return root.Handler(method, request);
            }
        }

        // MagicOnion のクライアントが返すのと同じく、レスポンスヘッダーを読める UnaryResult を作る
        private static class FakeResponse
        {
            public static UnaryResult<T> Success<T>(T value, Metadata responseHeaders = null)
            {
                var context = new FakeResponseContext<T>(Task.FromResult(value), responseHeaders ?? new Metadata());
                return new UnaryResult<T>(Task.FromResult<IResponseContext<T>>(context));
            }

            public static UnaryResult<T> Failure<T>(RpcException exception)
            {
                var context = new FakeResponseContext<T>(Task.FromException<T>(exception), new Metadata());
                return new UnaryResult<T>(Task.FromResult<IResponseContext<T>>(context));
            }
        }

        private sealed class FakeResponseContext<T> : IResponseContext<T>
        {
            public FakeResponseContext(Task<T> response, Metadata responseHeaders)
            {
                ResponseAsync = response;
                ResponseHeadersAsync = Task.FromResult(responseHeaders);
            }

            public Task<T> ResponseAsync { get; }

            public Task<Metadata> ResponseHeadersAsync { get; }

            public Type ResponseType => typeof(T);

            public Status GetStatus()
            {
                return Status.DefaultSuccess;
            }

            public Metadata GetTrailers()
            {
                return new Metadata();
            }

            public void Dispose()
            {
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

        // ダイアログを出さずに、受け取ったエラーを記録して Stay を返す
        private sealed class RecordingErrorHandler : ICommonErrorHandler
        {
            public List<ApiError> Errors { get; } = new();

            public UniTask<ErrorDecision> HandleAsync(ApiError error)
            {
                Errors.Add(error);
                return UniTask.FromResult(ErrorDecision.Stay);
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
    }
}
