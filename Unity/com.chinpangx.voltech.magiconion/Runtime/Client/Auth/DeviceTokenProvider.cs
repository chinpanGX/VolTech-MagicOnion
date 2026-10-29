using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Auth
{
    /// <summary>
    /// デバイス認証でアクセストークンを渡す。未登録ならデバイス登録し、トークンが無いか期限が近ければ発行し直す。
    /// </summary>
    public sealed class DeviceTokenProvider : ITokenProvider
    {
        // 期限ちょうどまで使うと、送っている間に切れることがあるので、早めに取り直す
        private const int RefreshMarginMinutes = 5;

        private readonly IDeviceService client;
        private readonly IAccountStore accountStore;
        private readonly ApiCoreOptions options;
        private readonly Func<DateTime> utcNow;

        // デバイス登録もトークンの発行も、同時に走ると device_id が2つ発行されたり、互いのトークンを無効にし合ったりするので1つずつ行う
        private readonly SemaphoreSlim gate = new(1, 1);

        // 下の値を読み書きするときのロック。Reset と Invalidate は gate を待たずに呼ばれるので、値の読み書きはこのロックの中で行う
        private readonly object stateLock = new();

        // Reset のたびに進める。gate の中の処理は始めたときの世代を覚え、終わるまでに変わっていたら(セッションを破棄されたら)結果を残さない
        private int generation;

        private AccountData account;

        // デバイス登録はできたが保存できなかったアカウント。登録し直すとサーバーに使われないプレイヤーが増えるので、次は保存からやり直す。
        // Reset では捨てない(タイトルに戻るだけで捨てると登録し直すことになる)。保存先にアカウントが入っていたら(引き継ぎなど)、そちらを使って捨てる
        private AccountData unsavedAccount;

        private string accessToken;
        private DateTime expiresAt;

        /// <summary>
        /// IDeviceService のクライアントと、アカウント情報の保存先を受け取って作る。
        /// </summary>
        /// <param name="client">MagicOnionClient.Create で作った IDeviceService のクライアント。</param>
        /// <param name="accountStore">アカウント情報の保存先。</param>
        /// <param name="options">ApiCore に渡すのと同じ設定。締め切りとアプリのバージョンを使う。</param>
        /// <param name="utcNow">今の時刻を返す処理。テストで差し替える。省略すると DateTime.UtcNow。</param>
        public DeviceTokenProvider(
            IDeviceService client,
            IAccountStore accountStore,
            ApiCoreOptions options,
            Func<DateTime> utcNow = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.accountStore = accountStore;
            this.options = options;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// 端末に保存したアカウント情報の状態を返す。通信はしない。タイトル画面で、最初の API を呼ぶ前に呼ぶ。
        /// </summary>
        /// <remarks>
        /// 保存していなければサインアップ、保存していればサインインに進む。ファイルが壊れていれば、例外にせず Corrupted を返す。
        /// </remarks>
        /// <exception cref="System.IO.IOException">ファイルを読めないとき(ほかのプロセスが開いている、など)。壊れているのではないので、もう一度試せる。</exception>
        /// <exception cref="UnauthorizedAccessException">ファイルを読む権限が無いとき。</exception>
        public async UniTask<AccountStatus> GetAccountStatusAsync()
        {
            await gate.WaitAsync();
            try
            {
                var startedGeneration = CurrentGeneration();
                AccountData loaded;
                try
                {
                    loaded = await LoadAccountAsync(startedGeneration);
                }
                catch (CorruptedAccountDataException)
                {
                    return AccountStatus.Corrupted;
                }

                // 登録したが保存できていないものは、サインアップの続きで保存するので、まだ保存されていないものとして扱う
                return loaded == null ? AccountStatus.NotRegistered : AccountStatus.Registered;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 有効なアクセストークンを返す。無ければ、デバイス登録やトークンの発行をしてから返す。
        /// </summary>
        /// <exception cref="CorruptedAccountDataException">保存したアカウント情報が読めないとき。</exception>
        /// <exception cref="AccountSaveFailedException">デバイス登録はできたが、保存できなかったとき。</exception>
        /// <exception cref="OperationCanceledException">保存する前に Reset されたとき。古いセッションの呼び出しなので、ApiCore が捨てる。</exception>
        public async UniTask<string> GetTokenAsync()
        {
            await gate.WaitAsync();
            try
            {
                int startedGeneration;
                lock (stateLock)
                {
                    if (accessToken != null && utcNow() < expiresAt.AddMinutes(-RefreshMarginMinutes))
                    {
                        return accessToken;
                    }

                    startedGeneration = generation;
                }

                var current = await LoadAccountAsync(startedGeneration) ?? await RegisterAndSaveAsync(startedGeneration);

                // Task は AsUniTask() を通さずにそのまま await する(本体の決まり)
                var issued = await client.WithOptions(CreateCallOptions()).IssueTokenAsync(current.DeviceId, current.SecretKey);
                lock (stateLock)
                {
                    // 発行している間に Reset されていたら、新しいセッションに前のアカウントのトークンを残さない
                    if (startedGeneration == generation)
                    {
                        accessToken = issued.AccessToken;
                        // MessagePack は DateTime を UTC で復元する
                        expiresAt = issued.ExpiresAt;
                    }
                }

                return issued.AccessToken;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 弾かれたトークンが今のトークンなら捨て、次の GetTokenAsync で発行し直させる。
        /// </summary>
        public void Invalidate(string rejectedToken)
        {
            lock (stateLock)
            {
                // 別の呼び出しがすでに発行し直していたら、新しいトークンは捨てない
                if (rejectedToken == null || rejectedToken == accessToken)
                {
                    accessToken = null;
                }
            }
        }

        /// <summary>
        /// 覚えているトークンとアカウント情報を捨てる。次の GetTokenAsync で保存先から読み直す。
        /// </summary>
        /// <remarks>
        /// 保存できていない登録は捨てない。次に保存先を読んで、アカウントが入っていれば(引き継ぎなど)そちらを使い、無ければ保存からやり直す。
        /// </remarks>
        public void Reset()
        {
            lock (stateLock)
            {
                generation++;
                accessToken = null;
                account = null;
            }
        }

        private int CurrentGeneration()
        {
            lock (stateLock)
            {
                return generation;
            }
        }

        // 覚えているアカウント情報か、保存先のアカウント情報を返す。どちらも無ければ null(登録したが保存できていないものも null)
        private async UniTask<AccountData> LoadAccountAsync(int startedGeneration)
        {
            lock (stateLock)
            {
                if (account != null)
                {
                    return account;
                }
            }

            var loaded = await accountStore.LoadAsync();
            if (loaded == null)
            {
                return null;
            }

            lock (stateLock)
            {
                // 保存先にアカウントがあれば、保存できていない登録より保存先を使う(引き継ぎなどで入れ替えた保存先を上書きしない)
                unsavedAccount = null;
                if (startedGeneration == generation)
                {
                    account = loaded;
                }
            }

            return loaded;
        }

        // 登録したのに保存できなかったものがあれば登録し直さずに保存からやり直し、無ければデバイス登録してから保存する
        private async UniTask<AccountData> RegisterAndSaveAsync(int startedGeneration)
        {
            AccountData pending;
            lock (stateLock)
            {
                pending = unsavedAccount;
            }

            if (pending == null)
            {
                pending = await RegisterAsync();
                lock (stateLock)
                {
                    unsavedAccount = pending;
                }
            }

            lock (stateLock)
            {
                // 登録している間に Reset されていたら、保存先が入れ替わっているかもしれないので書かない。次の呼び出しで保存先から読み直す
                if (startedGeneration != generation)
                {
                    throw new OperationCanceledException("The session was reset before saving the registered account.");
                }
            }

            await SaveAsync(pending);
            lock (stateLock)
            {
                if (unsavedAccount == pending)
                {
                    unsavedAccount = null;
                }

                if (startedGeneration == generation)
                {
                    account = pending;
                }
            }

            return pending;
        }

        private async UniTask<AccountData> RegisterAsync()
        {
            // サーバーは 16〜128 文字の表示できる ASCII 文字を受け付ける(IDeviceService.RegisterAsync)
            var secretKey = Guid.NewGuid().ToString();
            var registered = await client.WithOptions(CreateCallOptions()).RegisterAsync(secretKey);
            // player_id もデバイス登録で発行されるので、3つとも保存する
            return new AccountData(registered.DeviceId, secretKey, registered.PlayerId);
        }

        private async UniTask SaveAsync(AccountData registered)
        {
            try
            {
                await accountStore.SaveAsync(registered);
            }
            catch (Exception exception)
            {
                throw new AccountSaveFailedException(exception);
            }
        }

        // タイトル画面で最初に呼ぶので、ここで強制アップデートを判定してもらえるように、プラットフォームとバージョンを付ける
        private CallOptions CreateCallOptions()
        {
            var headers = new Metadata();
            options.AppVersion?.AddHeadersTo(headers);
            return new CallOptions(headers, DateTime.UtcNow.Add(options.Deadline));
        }
    }
}
