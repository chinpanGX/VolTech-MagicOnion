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
    /// ApiCore が、成功したレスポンスの共通レスポンス項目を反映処理へ渡すことを確かめる。
    /// </summary>
    [Description("ApiCore が、成功したレスポンスの共通レスポンス項目を反映処理へ渡すことを確かめる")]
    public class CommonResponseTests
    {
        private FakeCommonService service;
        private RecordingErrorHandler errorHandler;
        private List<string> applied;
        private ApiCore core;
        private CommonApi api;

        /// <summary>
        /// テストごとに、Common を返す偽のサービスと、反映処理を渡した ApiCore を作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            service = new FakeCommonService();
            service.Handler = () => Success(new CommonResult { Common = new CommonFields { Value = "v" } });
            errorHandler = new RecordingErrorHandler();
            applied = new List<string>();
            core = CreateBuilder().WithCommonResponseApplier(new RecordingApplier("common", applied)).Build();
            api = new CommonApi(core, service);
        }

        /// <summary>
        /// 反映処理は Common を受け取り、呼び出し側に返る前に反映が終わっている。
        /// </summary>
        [UnityTest]
        [Description("反映処理は Common を受け取り、呼び出し側に返る前に反映が終わっている")]
        public IEnumerator AppliesBeforeReturning() => UniTask.ToCoroutine(async () =>
        {
            var result = await api.DrawAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(applied, Is.EqualTo(new[] { "common:v" }));
        });

        /// <summary>
        /// Common が null のレスポンスでは、反映処理を呼ばない。
        /// </summary>
        [UnityTest]
        [Description("Common が null のレスポンスでは、反映処理を呼ばない")]
        public IEnumerator NullCommonIsNotApplied() => UniTask.ToCoroutine(async () =>
        {
            service.Handler = () => Success(new CommonResult());

            var result = await api.DrawAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(applied, Is.Empty);
        });

        /// <summary>
        /// 反映処理は省略できる。渡さなければ反映しない。
        /// </summary>
        [UnityTest]
        [Description("反映処理は省略できる。渡さなければ反映しない")]
        public IEnumerator ApplierIsOptional() => UniTask.ToCoroutine(async () =>
        {
            var withoutApplier = CreateBuilder().Build();

            var result = await new CommonApi(withoutApplier, service).DrawAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(applied, Is.Empty);
        });

        /// <summary>
        /// WithCommonResponseApplier に null を渡すと、設定済みの反映処理が外れる。
        /// </summary>
        [UnityTest]
        [Description("WithCommonResponseApplier に null を渡すと、設定済みの反映処理が外れる")]
        public IEnumerator NullApplierRemovesTheApplier() => UniTask.ToCoroutine(async () =>
        {
            var removed = CreateBuilder()
                .WithCommonResponseApplier(new RecordingApplier("common", applied))
                .WithCommonResponseApplier<CommonFields>(null)
                .Build();

            await new CommonApi(removed, service).DrawAsync(1);

            Assert.That(applied, Is.Empty);
        });

        /// <summary>
        /// WithCommonResponseApplier を 2 回呼ぶと、後の反映処理だけが動く。
        /// </summary>
        [UnityTest]
        [Description("WithCommonResponseApplier を 2 回呼ぶと、後の反映処理だけが動く")]
        public IEnumerator SecondApplierReplacesTheFirst() => UniTask.ToCoroutine(async () =>
        {
            var replaced = CreateBuilder()
                .WithCommonResponseApplier(new RecordingApplier("first", applied))
                .WithCommonResponseApplier(new RecordingApplier("second", applied))
                .Build();

            await new CommonApi(replaced, service).DrawAsync(1);

            Assert.That(applied, Is.EqualTo(new[] { "second:v" }));
        });

        /// <summary>
        /// IApiResponse を実装していないレスポンスは反映しない。
        /// </summary>
        [UnityTest]
        [Description("IApiResponse を実装していないレスポンスは反映しない")]
        public IEnumerator ResponseWithoutCommonIsNotApplied() => UniTask.ToCoroutine(async () =>
        {
            var result = await api.GetPlainAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(applied, Is.Empty);
        });

        /// <summary>
        /// 反映処理と違う TCommon のレスポンスは反映しない。
        /// </summary>
        [UnityTest]
        [Description("反映処理と違う TCommon のレスポンスは反映しない")]
        public IEnumerator OtherCommonTypeIsNotApplied() => UniTask.ToCoroutine(async () =>
        {
            var other = new OtherApplier();
            var otherCore = CreateBuilder().WithCommonResponseApplier(other).Build();

            var result = await new CommonApi(otherCore, service).DrawAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(other.Called, Is.False);
        });

        /// <summary>
        /// 反映処理で例外が起きたら、CLIENT_ERROR を共通エラーハンドラーに渡して失敗を返す。送り直さない。
        /// </summary>
        [UnityTest]
        [Description("反映処理で例外が起きたら、CLIENT_ERROR を共通エラーハンドラーに渡して失敗を返す")]
        public IEnumerator ApplierExceptionBecomesClientErrorWithoutResend() => UniTask.ToCoroutine(async () =>
        {
            var failing = CreateBuilder().WithCommonResponseApplier(new RecordingApplier("common", applied, fail: true)).Build();
            // エラーハンドラーが Retry を選んでも、サーバーの処理は成功しているので送り直さない
            errorHandler.Decisions.Enqueue(ErrorDecision.Retry);

            var result = await new CommonApi(failing, service).DrawAsync(1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(ReservedErrors.ClientError));
            Assert.That(errorHandler.Errors.Select(error => error.Code), Is.EqualTo(new[] { ReservedErrors.ClientError }));
            Assert.That(service.Calls, Is.EqualTo(1));
        });

        /// <summary>
        /// 通信中にセッションを捨てていたら、古いセッションのレスポンスなので反映しない。
        /// </summary>
        [UnityTest]
        [Description("通信中にセッションを捨てていたら、古いセッションのレスポンスなので反映しない")]
        public IEnumerator StaleResponseIsNotApplied() => UniTask.ToCoroutine(async () =>
        {
            service.Handler = () =>
            {
                core.ResetSession();
                return Success(new CommonResult { Common = new CommonFields { Value = "v" } });
            };

            var result = await api.DrawAsync(1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.IsDiscarded, Is.True);
            Assert.That(applied, Is.Empty);
        });

        private ApiCoreBuilder CreateBuilder()
        {
            return ApiCore.CreateBuilder(new ApiCoreOptions(), new FixedTokenProvider(), new ErrorPolicyProvider(Array.Empty<ErrorDefinition>()), errorHandler)
                .WithServerClock(new ServerClock(() => TimeSpan.Zero));
        }

        private static UnaryResult<T> Success<T>(T value)
        {
            var context = new FakeResponseContext<T>(value);
            return new UnaryResult<T>(Task.FromResult<IResponseContext<T>>(context));
        }

        /// <summary>
        /// アプリの共通レスポンス項目の型に当たる。
        /// </summary>
        public sealed class CommonFields
        {
            public string Value { get; set; }
        }

        public sealed class OtherFields
        {
        }

        // 受け取った Common を「名前:値」で記録する。fail なら記録してから例外を投げる
        private sealed class RecordingApplier : ICommonResponseApplier<CommonFields>
        {
            private readonly string name;
            private readonly List<string> applied;
            private readonly bool fail;

            public RecordingApplier(string name, List<string> applied, bool fail = false)
            {
                this.name = name;
                this.applied = applied;
                this.fail = fail;
            }

            public UniTask ApplyAsync(CommonFields common)
            {
                applied.Add($"{name}:{common.Value}");
                if (fail)
                {
                    throw new InvalidOperationException("apply failed");
                }

                return UniTask.CompletedTask;
            }
        }

        private sealed class OtherApplier : ICommonResponseApplier<OtherFields>
        {
            public bool Called { get; private set; }

            public UniTask ApplyAsync(OtherFields common)
            {
                Called = true;
                return UniTask.CompletedTask;
            }
        }

        private sealed class CommonApi : ApiServiceBase<ICommonTestService>
        {
            public CommonApi(ApiCore core, ICommonTestService client)
                : base(core, client)
            {
            }

            public UniTask<ApiResult<CommonResult>> DrawAsync(int count)
            {
                return InvokeAsync(nameof(ICommonTestService.DrawAsync), count, static (client, request) => client.DrawAsync(request));
            }

            public UniTask<ApiResult<PlainResult>> GetPlainAsync(int id)
            {
                return InvokeAsync(nameof(ICommonTestService.GetPlainAsync), id, static (client, request) => client.GetPlainAsync(request));
            }
        }

        // 通信する代わりに Handler を呼ぶ ICommonTestService。呼ばれた回数を数える
        private sealed class FakeCommonService : ICommonTestService
        {
            private readonly FakeCommonService root;

            public FakeCommonService()
            {
                root = this;
            }

            private FakeCommonService(FakeCommonService root)
            {
                this.root = root;
            }

            public int Calls { get; private set; }

            public Func<UnaryResult<CommonResult>> Handler { get; set; }

            public UnaryResult<CommonResult> DrawAsync(int count)
            {
                root.Calls++;
                return root.Handler();
            }

            public UnaryResult<PlainResult> GetPlainAsync(int id)
            {
                root.Calls++;
                return Success(new PlainResult());
            }

            public ICommonTestService WithOptions(CallOptions option)
            {
                return new FakeCommonService(root);
            }

            public ICommonTestService WithHeaders(Metadata headers)
            {
                return new FakeCommonService(root);
            }

            public ICommonTestService WithDeadline(DateTime deadline)
            {
                return new FakeCommonService(root);
            }

            public ICommonTestService WithCancellationToken(CancellationToken cancellationToken)
            {
                return new FakeCommonService(root);
            }

            public ICommonTestService WithHost(string host)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakeResponseContext<T> : IResponseContext<T>
        {
            public FakeResponseContext(T response)
            {
                ResponseAsync = Task.FromResult(response);
                ResponseHeadersAsync = Task.FromResult(new Metadata());
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

        private sealed class FixedTokenProvider : ITokenProvider
        {
            public UniTask<string> GetTokenAsync()
            {
                return UniTask.FromResult("token");
            }

            public void Invalidate(string rejectedToken)
            {
            }

            public void Reset()
            {
            }
        }

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

        /// <summary>
        /// テスト用のレスポンス。Common を持つ。
        /// </summary>
        public sealed class CommonResult : IApiResponse<CommonFields>
        {
            public CommonFields Common { get; set; }
        }

        /// <summary>
        /// テスト用のレスポンス。IApiResponse を実装しない。
        /// </summary>
        public sealed class PlainResult
        {
        }
    }

    /// <summary>
    /// CommonResponseTests で使う共有インターフェース。
    /// </summary>
    public interface ICommonTestService : IService<ICommonTestService>
    {
        /// <summary>共通レスポンス項目を返す、冪等性の対象の RPC。</summary>
        UnaryResult<CommonResponseTests.CommonResult> DrawAsync(int count);

        /// <summary>共通レスポンス項目を返さない RPC。</summary>
        [NoSideEffects]
        UnaryResult<CommonResponseTests.PlainResult> GetPlainAsync(int id);
    }
}
