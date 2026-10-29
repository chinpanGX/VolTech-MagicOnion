using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using MagicOnion;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Client.Auth;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// DeviceTokenProvider の、デバイス登録とトークンの発行の自動化を、サーバーなしで確かめる。
    /// </summary>
    [Description("DeviceTokenProvider の、デバイス登録とトークンの発行の自動化を、サーバーなしで確かめる")]
    public class DeviceTokenProviderTests
    {
        private const string RegisterMethod = nameof(IDeviceService.RegisterAsync);
        private const string IssueTokenMethod = nameof(IDeviceService.IssueTokenAsync);

        // static フィールドは持たない(Unity 7 は static フィールドに属性を求める。UAL0010)
        private readonly DateTime now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        private FakeDeviceService service;
        private InMemoryAccountStore accountStore;
        private DateTime currentTime;
        private DeviceTokenProvider provider;

        /// <summary>
        /// テストごとに、トークンの有効期限が1時間の偽の DeviceService を作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            currentTime = now;
            service = new FakeDeviceService();
            service.Register = async _ =>
            {
                // 同時に呼ばれたら重なるように、少し待ってから返す
                await UniTask.Yield();
                return new RegisterResult { DeviceId = "device-1", PlayerId = "player-1" };
            };
            service.IssueToken = async (_, _) =>
            {
                await UniTask.Yield();
                var issuedCount = service.Calls.Count(call => call.Method == IssueTokenMethod);
                return new IssueTokenResult { AccessToken = $"token-{issuedCount}", ExpiresAt = currentTime.AddHours(1) };
            };
            accountStore = new InMemoryAccountStore();
            provider = new DeviceTokenProvider(service, accountStore, new ApiCoreOptions(), () => currentTime);
        }

        /// <summary>
        /// 未登録なら、デバイス登録してからトークンを発行し、登録した device_id・secret_key・player_id を保存する。
        /// </summary>
        [UnityTest]
        [Description("未登録なら、デバイス登録してからトークンを発行し、登録した device_id・secret_key・player_id を保存する")]
        public IEnumerator RegistersDeviceWhenNotRegistered() => UniTask.ToCoroutine(async () =>
        {
            var token = await provider.GetTokenAsync();

            Assert.That(token, Is.EqualTo("token-1"));
            var saved = await accountStore.LoadAsync();
            Assert.That(saved.DeviceId, Is.EqualTo("device-1"));
            Assert.That(saved.PlayerId, Is.EqualTo("player-1"));
            var registeredSecretKey = service.Calls.First().SecretKey;
            Assert.That(saved.SecretKey, Is.EqualTo(registeredSecretKey));
            Assert.That(service.Calls.Last().SecretKey, Is.EqualTo(registeredSecretKey));
        });

        /// <summary>
        /// デバイス登録とトークンの発行にも、プラットフォームとバージョンと締め切りを付ける(タイトル画面で強制アップデートを判定してもらうため)。
        /// </summary>
        [UnityTest]
        [Description("デバイス登録とトークンの発行にも、プラットフォームとバージョンと締め切りを付ける")]
        public IEnumerator SendsAppVersion() => UniTask.ToCoroutine(async () =>
        {
            var options = new ApiCoreOptions(new AppVersion(AppVersion.Android, 42));
            provider = new DeviceTokenProvider(service, accountStore, options, () => currentTime);

            await provider.GetTokenAsync();

            Assert.That(service.Calls.Select(call => call.Method), Is.EqualTo(new[] { RegisterMethod, IssueTokenMethod }));
            foreach (var call in service.Calls)
            {
                Assert.That(call.Options.Headers.GetValue(ApiHeaders.AppPlatform), Is.EqualTo("android"), call.Method);
                Assert.That(call.Options.Headers.GetValue(ApiHeaders.AppVersion), Is.EqualTo("42"), call.Method);
                Assert.That(call.Options.Deadline, Is.Not.Null, call.Method);
            }
        });

        /// <summary>
        /// 同時に何回呼ばれても、デバイス登録とトークンの発行は1回ずつで、同じトークンが返る。
        /// </summary>
        [UnityTest]
        [Description("同時に何回呼ばれても、デバイス登録とトークンの発行は1回ずつで、同じトークンが返る")]
        public IEnumerator ConcurrentCallsRegisterAndIssueOnce() => UniTask.ToCoroutine(async () =>
        {
            var tokens = await UniTask.WhenAll(new[]
            {
                provider.GetTokenAsync(),
                provider.GetTokenAsync(),
                provider.GetTokenAsync(),
            });

            Assert.That(tokens, Is.All.EqualTo("token-1"));
            Assert.That(service.Calls.Count(call => call.Method == RegisterMethod), Is.EqualTo(1));
            Assert.That(service.Calls.Count(call => call.Method == IssueTokenMethod), Is.EqualTo(1));
        });

        /// <summary>
        /// 保存済みのアカウントがあれば、デバイス登録はせずにトークンだけ発行する。
        /// </summary>
        [UnityTest]
        [Description("保存済みのアカウントがあれば、デバイス登録はせずにトークンだけ発行する")]
        public IEnumerator SavedAccountSkipsRegister() => UniTask.ToCoroutine(async () =>
        {
            await accountStore.SaveAsync(new AccountData("saved-device", "saved-secret-key-0000", "saved-player"));

            await provider.GetTokenAsync();

            Assert.That(service.Calls.Single().DeviceId, Is.EqualTo("saved-device"));
        });

        /// <summary>
        /// 有効期限まで5分を切るまでは同じトークンを返し、切ったら発行し直す。
        /// </summary>
        [UnityTest]
        [Description("有効期限まで5分を切るまでは同じトークンを返し、切ったら発行し直す")]
        public IEnumerator ReissuesWhenNearExpiry() => UniTask.ToCoroutine(async () =>
        {
            var first = await provider.GetTokenAsync();

            currentTime = now.AddMinutes(54);
            Assert.That(await provider.GetTokenAsync(), Is.EqualTo(first));

            currentTime = now.AddMinutes(56);
            Assert.That(await provider.GetTokenAsync(), Is.EqualTo("token-2"));
        });

        /// <summary>
        /// サーバーに弾かれたトークンを Invalidate すると、次は発行し直す。古いトークンを Invalidate しても今のトークンは捨てない。
        /// </summary>
        [UnityTest]
        [Description("サーバーに弾かれたトークンを Invalidate すると、次は発行し直す。古いトークンを Invalidate しても今のトークンは捨てない")]
        public IEnumerator InvalidateReissuesOnlyCurrentToken() => UniTask.ToCoroutine(async () =>
        {
            var first = await provider.GetTokenAsync();
            provider.Invalidate(first);
            var second = await provider.GetTokenAsync();

            provider.Invalidate(first);

            Assert.That(second, Is.EqualTo("token-2"));
            Assert.That(await provider.GetTokenAsync(), Is.EqualTo(second));
        });

        /// <summary>
        /// 保存したアカウント情報が無ければ NotRegistered、あれば Registered を返す。どちらも通信しない。
        /// </summary>
        [UnityTest]
        [Description("保存したアカウント情報が無ければ NotRegistered、あれば Registered を返す。どちらも通信しない")]
        public IEnumerator StatusTellsWhetherAccountIsSaved() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(await provider.GetAccountStatusAsync(), Is.EqualTo(AccountStatus.NotRegistered));

            await accountStore.SaveAsync(new AccountData("saved-device", "saved-secret-key-0000", "saved-player"));

            Assert.That(await provider.GetAccountStatusAsync(), Is.EqualTo(AccountStatus.Registered));
            Assert.That(service.Calls, Is.Empty);
        });

        /// <summary>
        /// 保存したアカウント情報が読めなければ、例外にせず Corrupted を返す。
        /// </summary>
        [UnityTest]
        [Description("保存したアカウント情報が読めなければ、例外にせず Corrupted を返す")]
        public IEnumerator StatusIsCorruptedWhenUnreadable() => UniTask.ToCoroutine(async () =>
        {
            var store = new FlakyAccountStore { ThrowOnLoad = new CorruptedAccountDataException("account.bin", null) };
            provider = new DeviceTokenProvider(service, store, new ApiCoreOptions(), () => currentTime);

            Assert.That(await provider.GetAccountStatusAsync(), Is.EqualTo(AccountStatus.Corrupted));
        });

        /// <summary>
        /// デバイス登録の後に保存できなければ AccountSaveFailedException にし、トークンは発行しない。
        /// 次に呼ばれたときは登録し直さず、登録した結果の保存からやり直す。
        /// </summary>
        [UnityTest]
        [Description("デバイス登録の後に保存できなければ AccountSaveFailedException にし、トークンは発行しない。次に呼ばれたときは登録し直さず、登録した結果の保存からやり直す")]
        public IEnumerator SaveFailureRetriesSaveWithoutRegisteringAgain() => UniTask.ToCoroutine(async () =>
        {
            var store = new FlakyAccountStore { SaveFailures = 1 };
            provider = new DeviceTokenProvider(service, store, new ApiCoreOptions(), () => currentTime);

            AccountSaveFailedException thrown = null;
            try
            {
                await provider.GetTokenAsync();
            }
            catch (AccountSaveFailedException exception)
            {
                thrown = exception;
            }

            Assert.That(thrown, Is.Not.Null);
            Assert.That(thrown.InnerException, Is.InstanceOf<IOException>());
            Assert.That(service.Calls.Select(call => call.Method), Is.EqualTo(new[] { RegisterMethod }));
            Assert.That(await provider.GetAccountStatusAsync(), Is.EqualTo(AccountStatus.NotRegistered));

            var token = await provider.GetTokenAsync();

            Assert.That(token, Is.EqualTo("token-1"));
            Assert.That(service.Calls.Count(call => call.Method == RegisterMethod), Is.EqualTo(1), "登録し直した");
            Assert.That((await store.LoadAsync()).DeviceId, Is.EqualTo("device-1"));
        });

        /// <summary>
        /// 保存できなかった登録は、Reset(タイトルに戻る)の後も捨てず、次は登録し直さずに保存からやり直す。
        /// </summary>
        [UnityTest]
        [Description("保存できなかった登録は、Reset(タイトルに戻る)の後も捨てず、次は登録し直さずに保存からやり直す")]
        public IEnumerator ResetKeepsUnsavedRegistration() => UniTask.ToCoroutine(async () =>
        {
            var store = new FlakyAccountStore { SaveFailures = 1 };
            provider = new DeviceTokenProvider(service, store, new ApiCoreOptions(), () => currentTime);
            await ExpectSaveFailureAsync();

            provider.Reset();
            await provider.GetTokenAsync();

            Assert.That(service.Calls.Count(call => call.Method == RegisterMethod), Is.EqualTo(1), "登録し直した");
            Assert.That((await store.LoadAsync()).DeviceId, Is.EqualTo("device-1"));
        });

        /// <summary>
        /// 保存できなかった登録があっても、保存先にアカウントが入っていれば(引き継ぎなど)、そちらを使い、古い登録で上書きしない。
        /// </summary>
        [UnityTest]
        [Description("保存できなかった登録があっても、保存先にアカウントが入っていれば(引き継ぎなど)、そちらを使い、古い登録で上書きしない")]
        public IEnumerator ReplacedStoreWinsOverUnsavedRegistration() => UniTask.ToCoroutine(async () =>
        {
            var store = new FlakyAccountStore { SaveFailures = 1 };
            provider = new DeviceTokenProvider(service, store, new ApiCoreOptions(), () => currentTime);
            await ExpectSaveFailureAsync();

            await store.SaveAsync(new AccountData("transferred-device", "transferred-secret-0000", "transferred-player"));
            provider.Reset();
            await provider.GetTokenAsync();

            Assert.That(service.Calls.Last().DeviceId, Is.EqualTo("transferred-device"));
            Assert.That((await store.LoadAsync()).DeviceId, Is.EqualTo("transferred-device"));
            Assert.That(service.Calls.Count(call => call.Method == RegisterMethod), Is.EqualTo(1));
        });

        /// <summary>
        /// デバイス登録の間に Reset されたら、保存せずに打ち切る(保存先が入れ替わっているかもしれないため)。次は登録し直さずに保存する。
        /// </summary>
        [UnityTest]
        [Description("デバイス登録の間に Reset されたら、保存せずに打ち切る(保存先が入れ替わっているかもしれないため)。次は登録し直さずに保存する")]
        public IEnumerator ResetDuringRegistrationSkipsSave() => UniTask.ToCoroutine(async () =>
        {
            service.Register = _ =>
            {
                provider.Reset();
                return UniTask.FromResult(new RegisterResult { DeviceId = "device-1", PlayerId = "player-1" });
            };

            var canceled = false;
            try
            {
                await provider.GetTokenAsync();
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert.That(canceled, Is.True);
            Assert.That(await accountStore.LoadAsync(), Is.Null);
            await provider.GetTokenAsync();
            Assert.That(service.Calls.Count(call => call.Method == RegisterMethod), Is.EqualTo(1), "登録し直した");
            Assert.That((await accountStore.LoadAsync()).DeviceId, Is.EqualTo("device-1"));
        });

        /// <summary>
        /// トークンの発行の間に Reset されたら、そのトークンは覚えず、次は発行し直す(新しいセッションに前のトークンを残さない)。
        /// </summary>
        [UnityTest]
        [Description("トークンの発行の間に Reset されたら、そのトークンは覚えず、次は発行し直す(新しいセッションに前のトークンを残さない)")]
        public IEnumerator ResetDuringIssueDoesNotKeepToken() => UniTask.ToCoroutine(async () =>
        {
            await accountStore.SaveAsync(new AccountData("saved-device", "saved-secret-key-0000", "saved-player"));
            var issuedCount = 0;
            service.IssueToken = (_, _) =>
            {
                issuedCount++;
                if (issuedCount == 1)
                {
                    provider.Reset();
                }

                return UniTask.FromResult(new IssueTokenResult { AccessToken = $"token-{issuedCount}", ExpiresAt = currentTime.AddHours(1) });
            };

            await provider.GetTokenAsync();
            var second = await provider.GetTokenAsync();

            Assert.That(second, Is.EqualTo("token-2"));
        });

        private async UniTask ExpectSaveFailureAsync()
        {
            try
            {
                await provider.GetTokenAsync();
            }
            catch (AccountSaveFailedException)
            {
                return;
            }

            Assert.Fail("保存に失敗したのに例外にならなかった");
        }

        // FakeDeviceService に届いた1回の呼び出し
        private sealed class DeviceCall
        {
            public DeviceCall(string method, CallOptions options, string deviceId, string secretKey)
            {
                Method = method;
                Options = options;
                DeviceId = deviceId;
                SecretKey = secretKey;
            }

            public string Method { get; }

            public CallOptions Options { get; }

            public string DeviceId { get; }

            public string SecretKey { get; }
        }

        // 通信する代わりに、登録した処理を呼ぶ IDeviceService。WithOptions で渡されたヘッダーと締め切りを呼び出しと一緒に記録する
        private sealed class FakeDeviceService : IDeviceService
        {
            private readonly List<DeviceCall> calls;
            private readonly CallOptions options;
            private readonly FakeDeviceService root;

            public FakeDeviceService()
            {
                calls = new List<DeviceCall>();
                root = this;
            }

            private FakeDeviceService(FakeDeviceService root, CallOptions options)
            {
                calls = root.calls;
                this.root = root;
                this.options = options;
            }

            public IReadOnlyList<DeviceCall> Calls => calls;

            public Func<string, UniTask<RegisterResult>> Register { get; set; }

            public Func<string, string, UniTask<IssueTokenResult>> IssueToken { get; set; }

            public UnaryResult<RegisterResult> RegisterAsync(string secretKey)
            {
                calls.Add(new DeviceCall(RegisterMethod, options, null, secretKey));
                return new UnaryResult<RegisterResult>(root.Register(secretKey).AsTask());
            }

            public UnaryResult<IssueTokenResult> IssueTokenAsync(string deviceId, string secretKey)
            {
                calls.Add(new DeviceCall(IssueTokenMethod, options, deviceId, secretKey));
                return new UnaryResult<IssueTokenResult>(root.IssueToken(deviceId, secretKey).AsTask());
            }

            public IDeviceService WithOptions(CallOptions option)
            {
                return new FakeDeviceService(root, option);
            }

            public IDeviceService WithHeaders(Metadata headers)
            {
                return new FakeDeviceService(root, options.WithHeaders(headers));
            }

            public IDeviceService WithDeadline(DateTime deadline)
            {
                return new FakeDeviceService(root, options.WithDeadline(deadline));
            }

            public IDeviceService WithCancellationToken(CancellationToken cancellationToken)
            {
                return new FakeDeviceService(root, options.WithCancellationToken(cancellationToken));
            }

            public IDeviceService WithHost(string host)
            {
                throw new NotSupportedException();
            }
        }

        // 読み込みで例外を投げたり、決めた回数だけ保存に失敗したりする IAccountStore
        private sealed class FlakyAccountStore : IAccountStore
        {
            private readonly InMemoryAccountStore inner = new();

            public Exception ThrowOnLoad { get; set; }

            public int SaveFailures { get; set; }

            public UniTask<AccountData> LoadAsync()
            {
                return ThrowOnLoad != null ? throw ThrowOnLoad : inner.LoadAsync();
            }

            public UniTask SaveAsync(AccountData data)
            {
                if (SaveFailures > 0)
                {
                    SaveFailures--;
                    throw new IOException("disk full");
                }

                return inner.SaveAsync(data);
            }

            public UniTask ClearAsync()
            {
                return inner.ClearAsync();
            }
        }
    }
}
