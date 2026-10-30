using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using MagicOnion;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// API の呼び出しの共通処理。トークンとヘッダーの付与、順番待ち、再認証、エラーの変換とハンドラーの呼び出し、
    /// サーバー時刻の同期とゲーム内の日付の保持、進行中の呼び出しの通知を行う。
    /// </summary>
    /// <remarks>
    /// 処理の流れ: InvokeAsync(進行中の通知) → SendInQueueAsync(順番待ち) → SendWithRetryAsync(再認証・再送のループ) → SendOnceAsync(1回の送信)。
    /// 成功したら CompleteAsync でレスポンスヘッダーを反映し、失敗したら HandleErrorAsync でエラーハンドラーに渡す。
    /// 共通レスポンス項目(player_diff・バッジ・通知)の反映は、IApiResponse が決まるまで持たない(docs/design/client.md)。
    /// エラーハンドラーを呼ぶ間(エラーハンドラーの順番待ちを含む)は、その呼び出しの分を進行中から外し、Retry で送り直すときに戻す。
    /// エラーハンドラーがタイトル・ホームに戻ると決めたら、先にセッションを終わらせ(古い通信を止め)、
    /// 順番待ちを解放してから画面を遷移させる。遷移先の画面は、遷移の途中からでも新しいセッションで API を呼べる。
    /// </remarks>
    public sealed class ApiCore
    {
        private readonly ApiCoreOptions options;
        private readonly ITokenProvider tokenProvider;
        private readonly IErrorPolicyProvider errorPolicy;
        private readonly ICommonErrorHandler errorHandler;
        private readonly IErrorNavigator navigator;
        private readonly IReadOnlyList<IApiActivityHandler> activityHandlers;

        // null ならログを出さない。?. で呼ぶので、ログの文字列も作らない
        private readonly IApiLogger logger;
        private readonly ServerClock serverClock;

        // 冪等性の対象の RPC を1つずつ送るための順番待ち
        private readonly SemaphoreSlim requestQueue = new(1, 1);

        // エラーハンドラーを1つずつ呼ぶ(ダイアログを同時に出さない)ための順番待ち
        private readonly SemaphoreSlim errorHandlerQueue = new(1, 1);

        // 進行中の呼び出しの数。0 と 1 の間で変わったときにアクティビティのハンドラーを呼ぶ。
        // 数の更新とハンドラーの呼び出しを同じロックの中で行い、開始と終了が入れ違って届かないようにする
        private readonly object activityLock = new();
        private int activeCalls;

        // セッションの世代。タイトル・ホームに戻ったり、セッションを破棄したりするたびに増やす。
        // 呼び出しを始めたときの世代と今の世代が違えば、古いセッションの呼び出しなので、送らない・反映しない
        private int sessionEpoch;

        // セッションの通信を止めるためのキャンセル。セッションを終わらせるたびに Cancel して作り直す。
        // 送信中の通信と順番待ちが、締め切りを待たずにすぐ抜ける
        private CancellationTokenSource sessionCancellation = new();

        // セッションの最初に受け取ったゲーム内の日付。以降のリクエストに付け、サーバーは日付が変わっていれば DATE_CHANGED を返す。
        // 受け取るたびに更新すると日付の変化に気づけないので、セッションを破棄するまで最初の値を使う
        private string gameDate;

        // ApiCoreBuilder.Build から作る
        internal ApiCore(ApiCoreBuilder builder)
        {
            options = builder.Options;
            tokenProvider = builder.TokenProvider;
            errorPolicy = builder.ErrorPolicy;
            errorHandler = builder.ErrorHandler;
            logger = builder.Logger;
            serverClock = builder.ServerClock ?? new ServerClock();
            navigator = builder.Navigator;
            // ビルダーを使い回して足されても変わらないように写す
            activityHandlers = builder.ActivityHandlers.ToArray();
        }

        /// <summary>
        /// ApiCore を組み立てるビルダーを作る。任意の部品を With・Add で渡し、Build で ApiCore を作る。
        /// </summary>
        /// <param name="options">締め切りやアプリのバージョンなど、すべての呼び出しに使う設定。</param>
        /// <param name="tokenProvider">リクエストに付けるアクセストークンを渡す。UNAUTHENTICATED で弾かれたら、捨てて発行し直させる。</param>
        /// <param name="errorPolicy">業務エラーのコードを知っているかと、その既定の action を引く。知らないコードは UNKNOWN_ERROR として扱う。</param>
        /// <param name="errorHandler">handledCodes で受け取らなかったエラーを処理する。ダイアログを出し、その後の動き(送り直す・タイトルに戻るなど)を返す。</param>
        /// <returns>ApiCore のビルダー。</returns>
        /// <exception cref="ArgumentNullException">どれかの引数が null のとき。</exception>
        public static ApiCoreBuilder CreateBuilder(
            ApiCoreOptions options,
            ITokenProvider tokenProvider,
            IErrorPolicyProvider errorPolicy,
            ICommonErrorHandler errorHandler)
        {
            return new ApiCoreBuilder(options, tokenProvider, errorPolicy, errorHandler);
        }

        /// <summary>
        /// 今のサーバー時刻。成功したレスポンスのヘッダーで受け取ったサーバー時刻から計算する。
        /// </summary>
        public IServerClock ServerClock => serverClock;

        /// <summary>
        /// セッションを破棄する。覚えているトークン・アカウント情報・ゲーム内の日付を捨て、送信中・順番待ちのリクエストを止めて失敗にする。
        /// </summary>
        /// <remarks>
        /// アカウントの引き継ぎ・データ削除で、保存したアカウント情報を入れ替えた後に呼ぶ。
        /// </remarks>
        public void ResetSession()
        {
            var previous = BeginNewSession();
            Volatile.Write(ref gameDate, null);
            tokenProvider.Reset();
            // 止めた通信の後続が動く前に、捨てるものをすべて捨てておく
            previous.Cancel();
        }

        /// <summary>
        /// RPC を呼ぶ。失敗したときはエラーハンドラーの処理を済ませてから、失敗の結果を返す。
        /// </summary>
        /// <remarks>
        /// アプリのラッパーが、基底クラスの ApiServiceBase.InvokeAsync を通して呼ぶ。
        /// </remarks>
        /// <param name="settings">RPC ごとの設定。</param>
        /// <param name="client">MagicOnion のクライアント。呼ぶたびに WithOptions でヘッダーと締め切りを付けてから call に渡す。</param>
        /// <param name="request">RPC の引数。引数が複数ならタプルにまとめる。</param>
        /// <param name="handledCodes">呼び出し側が自分で処理するエラーコード。ダイアログを出さずに失敗として返る。</param>
        /// <param name="call">RPC を呼ぶ処理。キャプチャしない static ラムダで渡す。</param>
        /// <param name="notifyActivity">
        /// 進行中の呼び出しとしてアクティビティのハンドラーに知らせるか。画面を止めずに裏で呼ぶものは false にする。
        /// </param>
        internal async UniTask<ApiResult<TResponse>> InvokeAsync<TClient, TRequest, TResponse>(
            RpcSettings settings,
            TClient client,
            TRequest request,
            IEnumerable<ErrorCode> handledCodes,
            Func<TClient, TRequest, UnaryResult<TResponse>> call,
            bool notifyActivity = true)
            where TClient : IService<TClient>
        {
            // null なら知らせない。?. で呼ぶ
            var activity = notifyActivity ? new CallActivity(this) : null;
            activity?.Resume();

            (ApiResult<TResponse> Result, ErrorDecision Decision) outcome;
            try
            {
                outcome = await SendInQueueAsync(settings, client, request, handledCodes, call, activity);
            }
            finally
            {
                // 失敗・捨てた・例外のどれで抜けても終わらせる。終わらせないと、通信中の表示と入力ブロックが残り続ける。
                // エラーハンドラーの前に外したままなら、もう終わっているので何もしない
                activity?.Suspend();
            }

            // 画面の遷移は、順番待ちを解放し、進行中を終わらせてから行う。遷移先の画面がすぐに API を呼んでも待たされない
            await NavigateAsync(outcome.Decision);
            return outcome.Result;
        }

        // 冪等性の対象なら順番を待ってから送る。エラーハンドラーが決めた画面の遷移は、結果と一緒に返す
        private async UniTask<(ApiResult<TResponse> Result, ErrorDecision Decision)> SendInQueueAsync<TClient, TRequest, TResponse>(
            RpcSettings settings,
            TClient client,
            TRequest request,
            IEnumerable<ErrorCode> handledCodes,
            Func<TClient, TRequest, UnaryResult<TResponse>> call,
            CallActivity activity)
            where TClient : IService<TClient>
        {
            // 世代を先に読む。BeginNewSession はキャンセルを入れ替えてから世代を進めるので、
            // 新しい世代を読んだなら、キャンセルも必ず新しいものになる
            var epoch = Volatile.Read(ref sessionEpoch);
            var cancellationToken = Volatile.Read(ref sessionCancellation).Token;
            if (settings.IsIdempotencyTarget)
            {
                // 応答を受け取る前に次を送ると、サーバーに保存された最新の RequestID が上書きされ、再送で二重実行になる
                try
                {
                    await requestQueue.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 順番を待っている間にタイトル・ホームに戻ったので、送らずに捨てる
                    logger?.Log($"[Api] {settings.MethodName} discarded");
                    return (ApiResult<TResponse>.Failure(ApiError.Discarded()), ErrorDecision.Stay);
                }
            }

            try
            {
                if (IsStale(epoch))
                {
                    logger?.Log($"[Api] {settings.MethodName} discarded");
                    return (ApiResult<TResponse>.Failure(ApiError.Discarded()), ErrorDecision.Stay);
                }

                return await SendWithRetryAsync(settings, client, request, handledCodes, call, epoch, cancellationToken, activity);
            }
            finally
            {
                if (settings.IsIdempotencyTarget)
                {
                    requestQueue.Release();
                }
            }
        }

        // 成功するか、諦めるまで送る。送り直すのは次の2つのときだけ。
        // - UNAUTHENTICATED: トークンを取り直して1回だけ自動で送り直す
        // - エラーハンドラーが Retry を返した: 同じ RequestID で送り直す。エラーハンドラーの前に外した進行中を戻してから送る
        // エラーハンドラーが決めた画面の遷移は、結果と一緒に返し、順番待ちを解放してから InvokeAsync が行う
        private async UniTask<(ApiResult<TResponse> Result, ErrorDecision Decision)> SendWithRetryAsync<TClient, TRequest, TResponse>(
            RpcSettings settings,
            TClient client,
            TRequest request,
            IEnumerable<ErrorCode> handledCodes,
            Func<TClient, TRequest, UnaryResult<TResponse>> call,
            int epoch,
            CancellationToken cancellationToken,
            CallActivity activity)
            where TClient : IService<TClient>
        {
            // 再送では同じ RequestID を使うので、ループの外で採番する
            var requestId = settings.IsIdempotencyTarget ? Guid.NewGuid().ToString("N") : null;
            var reauthenticated = false;
            while (true)
            {
                var attempt = await SendOnceAsync(settings, client, request, call, requestId, canReauthenticate: !reauthenticated, cancellationToken);

                // 通信中にセッションを終わらせていたら(キャンセルで止まったときも)、結果を使わずに捨てる
                if (IsStale(epoch))
                {
                    logger?.Log($"[Api] {settings.MethodName} discarded after response");
                    return (ApiResult<TResponse>.Failure(ApiError.Discarded()), ErrorDecision.Stay);
                }

                if (attempt.Outcome == SendOutcome.Unauthenticated)
                {
                    // サーバーが処理を始める前に弾いたものなので、トークンを取り直して1回だけ送り直しても二重実行にならない
                    logger?.Log($"[Api] {settings.MethodName} UNAUTHENTICATED. reissue token and resend");
                    tokenProvider.Invalidate(attempt.Token);
                    reauthenticated = true;
                    continue;
                }

                if (attempt.Outcome == SendOutcome.Success)
                {
                    return Complete(settings, attempt.Response, attempt.ResponseHeaders, epoch);
                }

                var error = attempt.Error;
                logger?.Log($"[Api] {settings.MethodName} {error}");

                if (IsHandledByCaller(error, handledCodes))
                {
                    return (ApiResult<TResponse>.Failure(error), ErrorDecision.Stay);
                }

                var decision = await HandleErrorAsync(error, epoch, activity);
                if (decision == ErrorDecision.Retry)
                {
                    // エラーハンドラーの間にセッションを破棄していたら、送っても捨てるだけなので、進行中に戻さずに捨てる
                    if (IsStale(epoch))
                    {
                        logger?.Log($"[Api] {settings.MethodName} discarded");
                        return (ApiResult<TResponse>.Failure(ApiError.Discarded()), ErrorDecision.Stay);
                    }

                    activity?.Resume();
                    reauthenticated = false;
                    continue;
                }

                return (ApiResult<TResponse>.Failure(error), decision);
            }
        }

        // 1回だけ送る。例外は投げず、成功・再認証が要る・エラーのどれかを返す
        private async UniTask<SendAttempt<TResponse>> SendOnceAsync<TClient, TRequest, TResponse>(
            RpcSettings settings,
            TClient client,
            TRequest request,
            Func<TClient, TRequest, UnaryResult<TResponse>> call,
            string requestId,
            bool canReauthenticate,
            CancellationToken cancellationToken)
            where TClient : IService<TClient>
        {
            string token = null;
            try
            {
                if (settings.RequiresAuth)
                {
                    token = await tokenProvider.GetTokenAsync();
                }

                var callOptions = new CallOptions(
                    CreateHeaders(token, requestId),
                    DateTime.UtcNow.Add(options.Deadline),
                    cancellationToken);
                // Task は AsUniTask() を通さずにそのまま await する(本体の決まり)
                var rpc = call(client.WithOptions(callOptions), request);
                var response = await rpc.ResponseAsync;
                var responseHeaders = await rpc.ResponseHeadersAsync;
                // 通信のログは、応答を受け取ったときに1行のまとめだけを出す
                logger?.Log($"[Api] {settings.MethodName} OK");
                return SendAttempt<TResponse>.Success(response, responseHeaders);
            }
            catch (RpcException exception) when (
                exception.StatusCode == StatusCode.Unauthenticated && settings.RequiresAuth && canReauthenticate)
            {
                return SendAttempt<TResponse>.Unauthenticated(token);
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated && settings.RequiresAuth)
            {
                // 取り直したトークンも弾かれた。残すと、エラーハンドラーの Retry やほかの RPC が同じトークンでまた弾かれる
                tokenProvider.Invalidate(token);
                return SendAttempt<TResponse>.Failure(ConvertError(settings, exception));
            }
            catch (Exception exception)
            {
                return SendAttempt<TResponse>.Failure(ConvertError(settings, exception));
            }
        }

        // 受け取った例外をエラーコードにする。catch 句の中で変換すると、変換で起きた例外は他の catch 句で捕まらずに
        // 呼び出し側まで抜けるので、変換の失敗もここで CLIENT_ERROR にする
        private ApiError ConvertError(RpcSettings settings, Exception exception)
        {
            if (exception is RpcException rpcException)
            {
                try
                {
                    return ErrorConverter.FromRpcException(rpcException, errorPolicy);
                }
                catch (Exception conversionException)
                {
                    logger?.LogException($"[Api] {settings.MethodName} failed to convert {rpcException.Status}", conversionException);
                    return ErrorConverter.FromClientException(conversionException, errorPolicy);
                }
            }

            logger?.LogException($"[Api] {settings.MethodName} failed in client", exception);
            return ErrorConverter.FromClientException(exception, errorPolicy);
        }

        // 成功したレスポンスの後始末。古いセッションのものでなければ、レスポンスヘッダーを反映して返す
        private (ApiResult<TResponse> Result, ErrorDecision Decision) Complete<TResponse>(
            RpcSettings settings,
            TResponse response,
            Metadata responseHeaders,
            int epoch)
        {
            // 通信中にタイトル・ホームに戻ったりセッションを捨てたりしていたら、古いセッションのレスポンスなので
            // 反映しない。反映すると、その後にサインインで置き換えた新しいデータを上書きしてしまう
            if (IsStale(epoch))
            {
                logger?.Log($"[Api] {settings.MethodName} discarded after response");
                return (ApiResult<TResponse>.Failure(ApiError.Discarded()), ErrorDecision.Stay);
            }

            ApplyResponseHeaders(responseHeaders);
            return (ApiResult<TResponse>.Success(response), ErrorDecision.Stay);
        }

        // 呼び出し側が処理するエラーか。予約コードは画面ごとに扱いを変えないように、handledCodes にあっても共通エラーハンドラーに回す。
        // ただし端末のアカウント情報の読み書きの失敗は、通信のエラーではなく、タイトル画面の流れで専用の画面を出すので、いつも呼び出し側に返す
        private static bool IsHandledByCaller(ApiError error, IEnumerable<ErrorCode> handledCodes)
        {
            if (ErrorConverter.IsAccountStorageError(error.Code))
            {
                return true;
            }

            return !ErrorConverter.IsReserved(error.Code) && handledCodes != null && handledCodes.Contains(error.Code);
        }

        private Metadata CreateHeaders(string token, string requestId)
        {
            var headers = new Metadata();
            if (token != null)
            {
                headers.Add(ApiHeaders.Authorization, $"Bearer {token}");
            }

            if (requestId != null)
            {
                headers.Add(ApiHeaders.RequestId, requestId);
            }

            options.AppVersion?.AddHeadersTo(headers);

            // サインアップ・サインインなど、セッションの最初の呼び出しでは日付をまだ持っていないので付けない
            var date = Volatile.Read(ref gameDate);
            if (date != null)
            {
                headers.Add(ApiHeaders.GameDate, date);
            }

            return headers;
        }

        // サーバー時刻を記録し、ゲーム内の日付をまだ持っていなければ覚える
        private void ApplyResponseHeaders(Metadata responseHeaders)
        {
            var serverTime = responseHeaders?.GetValue(ApiHeaders.ServerTime);
            if (long.TryParse(serverTime, out var unixMilliseconds))
            {
                serverClock.Synchronize(DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds));
            }

            var date = responseHeaders?.GetValue(ApiHeaders.GameDate);
            if (!string.IsNullOrEmpty(date))
            {
                Interlocked.CompareExchange(ref gameDate, date, null);
            }
        }

        // エラーハンドラーを呼ぶ前に、この呼び出しの分を進行中から外す。通信はもう終わっていて、ダイアログなどでユーザーの
        // 操作を待つだけなので、通信中の表示をダイアログの後ろに残さない。ほかのダイアログを待つ順番待ちの間も外したままにする。
        // Retry で送り直すときは、呼び出し側(SendWithRetryAsync)が戻す
        private async UniTask<ErrorDecision> HandleErrorAsync(ApiError error, int epoch, CallActivity activity)
        {
            activity?.Suspend();
            await errorHandlerQueue.WaitAsync();
            try
            {
                // 前のダイアログでタイトル・ホームに戻っていたら、ダイアログを出さずに失敗にする
                if (IsStale(epoch))
                {
                    return ErrorDecision.Stay;
                }

                ErrorDecision decision;
                try
                {
                    decision = await errorHandler.HandleAsync(error);
                }
                catch (Exception exception)
                {
                    logger?.LogException("[Api] error handler failed", exception);
                    decision = ErrorDecision.Stay;
                }

                // 画面を遷移する前に、セッションを終わらせる(古い通信を止め、次のエラーのダイアログも出さない)。
                // 遷移は、順番待ちを解放してから InvokeAsync が行う
                switch (decision)
                {
                    case ErrorDecision.ReturnToHome:
                        BeginNewSession().Cancel();
                        break;
                    case ErrorDecision.ReturnToTitle:
                        ResetSession();
                        break;
                }

                return decision;
            }
            finally
            {
                errorHandlerQueue.Release();
            }
        }

        // 呼び出しを始めたときから、セッションの世代が変わったか
        private bool IsStale(int epoch)
        {
            return epoch != Volatile.Read(ref sessionEpoch);
        }

        // 新しいセッションにする。キャンセルを入れ替えてから世代を進め、止めるための古いキャンセルを返す。
        // 古いキャンセルは、まだそのトークンを持つ通信があるので Dispose しない(タイマーを持たないので GC で回収される)
        private CancellationTokenSource BeginNewSession()
        {
            var previous = Interlocked.Exchange(ref sessionCancellation, new CancellationTokenSource());
            Interlocked.Increment(ref sessionEpoch);
            return previous;
        }

        // 進行中の呼び出しを1つ増やし、0 から 1 になったら開始を登録した順に知らせる
        private void BeginActivity()
        {
            lock (activityLock)
            {
                activeCalls++;
                if (activeCalls != 1)
                {
                    return;
                }

                for (var i = 0; i < activityHandlers.Count; i++)
                {
                    try
                    {
                        activityHandlers[i].OnActivityStarted();
                    }
                    catch (Exception exception)
                    {
                        logger?.LogException("[Api] activity handler failed on start", exception);
                    }
                }
            }
        }

        // 進行中の呼び出しを1つ減らし、1 から 0 になったら終了を逆の順に知らせる。
        // 1つのハンドラーが例外を投げても、残りのハンドラーの終了(入力ブロックの解除など)は必ず呼ぶ
        private void EndActivity()
        {
            lock (activityLock)
            {
                activeCalls--;
                if (activeCalls != 0)
                {
                    return;
                }

                for (var i = activityHandlers.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        activityHandlers[i].OnActivityEnded();
                    }
                    catch (Exception exception)
                    {
                        logger?.LogException("[Api] activity handler failed on end", exception);
                    }
                }
            }
        }

        // エラーハンドラーが決めた画面の遷移をする。遷移しない動き(Retry・Stay)なら何もしない
        private async UniTask NavigateAsync(ErrorDecision decision)
        {
            if (navigator == null)
            {
                return;
            }

            try
            {
                switch (decision)
                {
                    case ErrorDecision.ReturnToTitle:
                        await navigator.GoToTitleAsync();
                        break;
                    case ErrorDecision.ReturnToHome:
                        await navigator.GoToHomeAsync();
                        break;
                    case ErrorDecision.OpenStore:
                        await navigator.OpenStoreAsync();
                        break;
                }
            }
            catch (Exception exception)
            {
                logger?.LogException($"[Api] navigation to {decision} failed", exception);
            }
        }

        // 1回の呼び出しの分の進行中。外す・戻すを何度呼んでも、ApiCore の進行中の数を1より多く増やしたり、
        // 増やしていないのに減らしたりしない。1回の呼び出しの処理は順に進むので、ロックは要らない
        private sealed class CallActivity
        {
            private readonly ApiCore core;
            private bool active;

            public CallActivity(ApiCore core)
            {
                this.core = core;
            }

            // 進行中に数える。もう数えていれば何もしない
            public void Resume()
            {
                if (active)
                {
                    return;
                }

                active = true;
                core.BeginActivity();
            }

            // 進行中から外す。もう外していれば何もしない
            public void Suspend()
            {
                if (!active)
                {
                    return;
                }

                active = false;
                core.EndActivity();
            }
        }

        private enum SendOutcome
        {
            Success,
            Unauthenticated,
            Failure,
        }

        // 1回の送信の結果。SendOnceAsync の中だけで使う
        private readonly struct SendAttempt<TResponse>
        {
            private SendAttempt(SendOutcome outcome, TResponse response, Metadata responseHeaders, string token, ApiError error)
            {
                Outcome = outcome;
                Response = response;
                ResponseHeaders = responseHeaders;
                Token = token;
                Error = error;
            }

            public SendOutcome Outcome { get; }

            // Success のときのレスポンス
            public TResponse Response { get; }

            // Success のときのレスポンスヘッダー
            public Metadata ResponseHeaders { get; }

            // Unauthenticated のときに、サーバーに弾かれたトークン
            public string Token { get; }

            // Failure のときのエラー
            public ApiError Error { get; }

            public static SendAttempt<TResponse> Success(TResponse response, Metadata responseHeaders)
            {
                return new SendAttempt<TResponse>(SendOutcome.Success, response, responseHeaders, null, null);
            }

            public static SendAttempt<TResponse> Unauthenticated(string token)
            {
                return new SendAttempt<TResponse>(SendOutcome.Unauthenticated, default, null, token, null);
            }

            public static SendAttempt<TResponse> Failure(ApiError error)
            {
                return new SendAttempt<TResponse>(SendOutcome.Failure, default, null, null, error);
            }
        }
    }
}
