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
    /// ApiCore が、成功したレスポンスの共通レスポンス項目を player_diff → バッジ → 通知の順に反映処理へ渡すことを確かめる。
    /// </summary>
    [Description("ApiCore が、成功したレスポンスの共通レスポンス項目を player_diff → バッジ → 通知の順に反映処理へ渡すことを確かめる")]
    public class CommonResponseTests
    {
        private FakeCommonService service;
        private RecordingErrorHandler errorHandler;
        private List<string> applied;
        private ApiCore core;
        private CommonApi api;

        /// <summary>
        /// テストごとに、Common を返す偽のサービスと、3 つの反映処理を渡した ApiCore を作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            service = new FakeCommonService();
            service.Handler = () => Success(new CommonResult { Common = new CommonFields { Value = "v" } });
            errorHandler = new RecordingErrorHandler();
            applied = new List<string>();
            core = CreateBuilder()
                .WithPlayerDiffApplier(new RecordingApplier("player_diff", applied))
                .WithBadgeApplier(new RecordingApplier("badges", applied))
                .WithNotificationApplier(new RecordingApplier("notifications", applied))
                .Build();
            api = new CommonApi(core, service);
        }

        /// <summary>
        /// 反映処理は player_diff → バッジ → 通知の順に、同じ Common を受け取る。呼び出し側には反映の後で返る。
        /// </summary>
        [UnityTest]
        [Description("反映処理は player_diff → バッジ → 通知の順に、同じ Common を受け取る")]
        public IEnumerator AppliesInOrder() => UniTask.ToCoroutine(async () =>
        {
            var result = await api.DrawAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(applied, Is.EqualTo(new[] { "player_diff:v", "badges:v", "notifications:v" }));
        });

        /// <summary>
        /// Common が null のレスポンスでは、どの反映処理も呼ばない。
        /// </summary>
        [UnityTest]
        [Description("Common が null のレスポンスでは、どの反映処理も呼ばない")]
        public IEnumerator NullCommonIsNotApplied() => UniTask.ToCoroutine(async () =>
        {
            service.Handler = () => Success(new CommonResult());

            var result = await api.DrawAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(applied, Is.Empty);
        });

        /// <summary>
        /// 反映処理は省略できる。渡したものだけを、決まった順で呼ぶ。
        /// </summary>
        [UnityTest]
        [Description("反映処理は省略できる。渡したものだけを、決まった順で呼ぶ")]
        public IEnumerator OnlyGivenAppliersAreCalled() => UniTask.ToCoroutine(async () =>
        {
            var partial = CreateBuilder()
                .WithNotificationApplier(new RecordingApplier("notifications", applied))
                .WithPlayerDiffApplier(new RecordingApplier("player_diff", applied))
                .Build();

            var result = await new CommonApi(partial, service).DrawAsync(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(applied, Is.EqualTo(new[] { "player_diff:v", "notifications:v" }));
        });

        /// <summary>
        /// 反映処理で例外が起きたら、残りは呼ばず、CLIENT_ERROR を共通エラーハンドラーに渡して失敗を返す。送り直さない。
        /// </summary>
        [UnityTest]
        [Description("反映処理で例外が起きたら、残りは呼ばず、CLIENT_ERROR を共通エラーハンドラーに渡して失敗を返す")]
        public IEnumerator ApplierExceptionBecomesClientErrorWithoutResend() => UniTask.ToCoroutine(async () =>
        {
            var failing = CreateBuilder()
                .WithPlayerDiffApplier(new RecordingApplier("player_diff", applied))
                .WithBadgeApplier(new RecordingApplier("badges", applied, fail: true))
                .WithNotificationApplier(new RecordingApplier("notifications", applied))
                .Build();
            // エラーハンドラーが Retry を選んでも、サーバーの処理は成功しているので送り直さない
            errorHandler.Decisions.Enqueue(ErrorDecision.Retry);

            var result = await new CommonApi(failing, service).DrawAsync(1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(ReservedErrors.ClientError));
            Assert.That(errorHandler.Errors.Select(error => error.Code), Is.EqualTo(new[] { ReservedErrors.ClientError }));
            Assert.That(applied, Is.EqualTo(new[] { "player_diff:v", "badges:v" }));
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

        /// <summary>
        /// 3 つの反映処理の TCommon が食い違っていたら、Build で例外にする。
        /// </summary>
        [Test]
        [Description("3 つの反映処理の TCommon が食い違っていたら、Build で例外にする")]
        public void MismatchedCommonTypesThrowOnBuild()
        {
            var builder = CreateBuilder()
                .WithPlayerDiffApplier(new RecordingApplier("player_diff", applied))
                .WithBadgeApplier(new OtherApplier());

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

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

        private sealed class OtherFields
        {
        }

        // 受け取った Common を「名前:値」で記録する。fail なら記録してから例外を投げる
        private sealed class RecordingApplier : IPlayerDiffApplier<CommonFields>, IBadgeApplier<CommonFields>, INotificationApplier<CommonFields>
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

        private sealed class OtherApplier : IBadgeApplier<OtherFields>
        {
            public UniTask ApplyAsync(OtherFields common)
            {
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
    }

    /// <summary>
    /// CommonResponseTests で使う共有インターフェース。
    /// </summary>
    public interface ICommonTestService : IService<ICommonTestService>
    {
        /// <summary>共通レスポンス項目を返す、冪等性の対象の RPC。</summary>
        UnaryResult<CommonResponseTests.CommonResult> DrawAsync(int count);
    }
}
