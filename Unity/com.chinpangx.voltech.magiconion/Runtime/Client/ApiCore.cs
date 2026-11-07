using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using MagicOnion;
using MagicOnion.Client;
using MagicOnion.Serialization;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// API の呼び出しの共通処理。CreateClient で作ったクライアントの呼び出しが、IClientFilter から届く。トークンの取得、順番待ち、再認証、エラーの変換とハンドラーの呼び出し、
    /// サーバー時刻の同期とゲーム内の日付の保持、進行中の呼び出しの通知を行う。
    /// </summary>
    /// <remarks>
    /// 処理の流れ: SendAsync(進行中の通知) → SendInQueueAsync(順番待ち) → SendWithRetryAsync(再認証・再送のループ) → SendOnceAsync(1回の送信)。
    /// ヘッダー・締め切り・キャンセルは ApiCallInvoker が試行ごとに付ける。
    /// 成功したら CompleteAsync でレスポンスヘッダーと共通レスポンス項目を反映し、
    /// 失敗したら HandleErrorAsync でエラーハンドラーに渡す。
    /// エラーハンドラーを呼ぶ間(エラーハンドラーの順番待ちを含む)は、その呼び出しの分を進行中から外し、Retry で送り直すときに戻す。
    /// エラーハンドラーがタイトル・ホームに戻ると決めたら、先にセッションを終わらせ(古い通信を止め)、
    /// 順番待ちを解放してから画面を遷移させる。遷移先の画面は、遷移の途中からでも新しいセッションで API を呼べる。
    /// 失敗は、これらの処理を済ませてから ApiException で投げる。アプリは ToResult で受け取る。
    /// </remarks>
    public sealed class ApiCore
    {
        private readonly ApiCallInvoker callInvoker;
        private readonly IMagicOnionSerializerProvider serializerProvider;
        private readonly IMagicOnionClientFactoryProvider clientFactoryProvider;
        private readonly ITokenProvider tokenProvider;
        private readonly IErrorPolicyProvider errorPolicy;
        private readonly ICommonErrorHandler errorHandler;
        private readonly IErrorNavigator navigator;
        private readonly IReadOnlyList<IApiActivityHandler> activityHandlers;

        // 共通レスポンス項目の反映処理。null なら反映しない
        private readonly Func<object, UniTask> applyCommonResponse;

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
            callInvoker = new ApiCallInvoker(builder.CallInvoker, builder.Options);
            serializerProvider = builder.SerializerProvider;
            clientFactoryProvider = builder.ClientFactoryProvider;
            tokenProvider = builder.TokenProvider;
            errorPolicy = builder.ErrorPolicy;
            errorHandler = builder.ErrorHandler;
            logger = builder.Logger;
            serverClock = builder.ServerClock ?? new ServerClock();
            navigator = builder.Navigator;
            // ビルダーを使い回して足されても変わらないように写す
            activityHandlers = builder.ActivityHandlers.ToArray();
            applyCommonResponse = builder.ApplyCommonResponse;
        }

        /// <summary>
        /// ApiCore を組み立てるビルダーを作る。任意の部品を With・Add で渡し、Build で ApiCore を作る。
        /// </summary>
        /// <param name="callInvoker">通信に使う CallInvoker。ApiChannel.CreateCallInvoker で作る。</param>
        /// <param name="serializerProvider">アプリの生成物の Resolver を使うシリアライザー。</param>
        /// <param name="clientFactoryProvider">[MagicOnionClientGeneration] が作った ClientFactoryProvider。</param>
        /// <param name="options">締め切りやアプリのバージョンなど、すべての呼び出しに使う設定。</param>
        /// <param name="tokenProvider">リクエストに付けるアクセストークンを渡す。UNAUTHENTICATED で弾かれたら、捨てて発行し直させる。</param>
        /// <param name="errorPolicy">業務エラーのコードを知っているかと、その既定の action を引く。知らないコードは UNKNOWN_ERROR として扱う。</param>
        /// <param name="errorHandler">WithHandledCodes で指定しなかったエラーを処理する。ダイアログを出し、その後の動き(送り直す・タイトルに戻るなど)を返す。</param>
        /// <returns>ApiCore のビルダー。</returns>
        /// <exception cref="ArgumentNullException">どれかの引数が null のとき。</exception>
        public static ApiCoreBuilder CreateBuilder(
            CallInvoker callInvoker,
            IMagicOnionSerializerProvider serializerProvider,
            IMagicOnionClientFactoryProvider clientFactoryProvider,
            ApiCoreOptions options,
            ITokenProvider tokenProvider,
            IErrorPolicyProvider errorPolicy,
            ICommonErrorHandler errorHandler)
        {
            return new ApiCoreBuilder(callInvoker, serializerProvider, clientFactoryProvider, options, tokenProvider, errorPolicy, errorHandler);
        }

        /// <summary>
        /// 共有インターフェースのクライアントを作る。アプリはこれを直接呼び、ToResult で結果を受け取る。
        /// </summary>
        /// <typeparam name="T">共有インターフェース。[MagicOnionClientGeneration] の対象のアセンブリのもの。</typeparam>
        /// <returns>基盤のフィルターを通るクライアント。</returns>
        /// <exception cref="NotSupportedException">T のアセンブリが [MagicOnionClientGeneration] の対象でないとき。</exception>
        public T CreateClient<T>()
            where T : IService<T>
        {
            var filter = new ApiCallFilter(this, RpcSettings.CreateAll(typeof(T)));
            return MagicOnionClient.Create<T>(callInvoker, serializerProvider, new IClientFilter[] { filter }, clientFactoryProvider);
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
        /// RPC を呼ぶ。失敗したときはエラーハンドラーの処理を済ませてから、ApiException を投げる。ApiCallFilter から呼ばれる。
        /// </summary>
        /// <param name="settings">RPC ごとの設定。</param>
        /// <param name="context">フィルターに届いた呼び出し。</param>
        /// <param name="next">送る処理。再送では複数回呼ぶ。</param>
        /// <param name="handledCodes">呼び出し側が自分で処理するエラーコード。ダイアログを出さずに失敗として返る。</param>
        /// <param name="notifyActivity">
        /// 進行中の呼び出しとしてアクティビティのハンドラーに知らせるか。画面を止めずに裏で呼ぶものは false にする。
        /// </param>
        internal async ValueTask<ResponseContext> SendAsync(
            RpcSettings settings,
            RequestContext context,
            Func<RequestContext, ValueTask<ResponseContext>> next,
            IEnumerable<ErrorCode> handledCodes,
            bool notifyActivity)
        {
            // null なら知らせない。?. で呼ぶ
            var activity = notifyActivity ? new CallActivity(this) : null;
            activity?.Resume();

            SendOutput output;
            try
            {
                output = await SendInQueueAsync(settings, context, next, handledCodes, activity);
            }
            finally
            {
                // 失敗・捨てた・例外のどれで抜けても終わらせる。終わらせないと、通信中の表示と入力ブロックが残り続ける。
                // エラーハンドラーの前に外したままなら、もう終わっているので何もしない
                activity?.Suspend();
            }

            // 画面の遷移は、順番待ちを解放し、進行中を終わらせてから行う。遷移先の画面がすぐに API を呼んでも待たされない
            await NavigateAsync(output.Decision);
            if (output.Error != null)
            {
                throw new ApiException(output.Error);
            }

            return output.Response;
        }

        // 冪等性の対象なら順番を待ってから送る。エラーハンドラーが決めた画面の遷移は、結果と一緒に返す
        private async UniTask<SendOutput> SendInQueueAsync(
            RpcSettings settings,
            RequestContext context,
            Func<RequestContext, ValueTask<ResponseContext>> next,
            IEnumerable<ErrorCode> handledCodes,
            CallActivity activity)
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
                    return SendOutput.Failure(ApiError.Discarded(), ErrorDecision.Stay);
                }
            }

            try
            {
                if (IsStale(epoch))
                {
                    logger?.Log($"[Api] {settings.MethodName} discarded");
                    return SendOutput.Failure(ApiError.Discarded(), ErrorDecision.Stay);
                }

                return await SendWithRetryAsync(settings, context, next, handledCodes, epoch, cancellationToken, activity);
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
        // エラーハンドラーが決めた画面の遷移は、結果と一緒に返し、順番待ちを解放してから SendAsync が行う
        private async UniTask<SendOutput> SendWithRetryAsync(
            RpcSettings settings,
            RequestContext context,
            Func<RequestContext, ValueTask<ResponseContext>> next,
            IEnumerable<ErrorCode> handledCodes,
            int epoch,
            CancellationToken cancellationToken,
            CallActivity activity)
        {
            // 再送では同じ RequestID を使うので、ループの外で採番する
            var requestId = settings.IsIdempotencyTarget ? Guid.NewGuid().ToString("N") : null;
            var reauthenticated = false;
            while (true)
            {
                var attempt = await SendOnceAsync(settings, context, next, requestId, canReauthenticate: !reauthenticated, cancellationToken);

                // 通信中にセッションを終わらせていたら(キャンセルで止まったときも)、結果を使わずに捨てる
                if (IsStale(epoch))
                {
                    logger?.Log($"[Api] {settings.MethodName} discarded after response");
                    return SendOutput.Failure(ApiError.Discarded(), ErrorDecision.Stay);
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
                    return await CompleteAsync(settings, context, attempt, epoch, activity);
                }

                // 取り直したトークンも弾かれた。残すと、エラーハンドラーの Retry やほかの RPC が同じトークンでまた弾かれる
                if (attempt.Token != null)
                {
                    tokenProvider.Invalidate(attempt.Token);
                }

                var error = attempt.Error;
                logger?.Log($"[Api] {settings.MethodName} {error}");

                if (IsHandledByCaller(error, handledCodes))
                {
                    return SendOutput.Failure(error, ErrorDecision.Stay);
                }

                var decision = await HandleErrorAsync(error, epoch, activity);
                if (decision == ErrorDecision.Retry)
                {
                    // エラーハンドラーの間にセッションを破棄していたら、送っても捨てるだけなので、進行中に戻さずに捨てる
                    if (IsStale(epoch))
                    {
                        logger?.Log($"[Api] {settings.MethodName} discarded");
                        return SendOutput.Failure(ApiError.Discarded(), ErrorDecision.Stay);
                    }

                    activity?.Resume();
                    reauthenticated = false;
                    continue;
                }

                return SendOutput.Failure(error, decision);
            }
        }

        // 1回だけ送る。例外は投げず、成功・再認証が要る・エラーのどれかを返す
        private async UniTask<SendAttempt> SendOnceAsync(
            RpcSettings settings,
            RequestContext context,
            Func<RequestContext, ValueTask<ResponseContext>> next,
            string requestId,
            bool canReauthenticate,
            CancellationToken cancellationToken)
        {
            string token = null;
            try
            {
                if (settings.RequiresAuth)
                {
                    token = await tokenProvider.GetTokenAsync();
                }

                var callContext = new ApiCallContext(token, requestId, Volatile.Read(ref gameDate), cancellationToken);
                // ValueTask は AsUniTask() を通さずにそのまま await する(本体の決まり)
                var response = await callInvoker.Send(callContext, context, next);
                var responseHeaders = await response.ResponseHeadersAsync;
                // 通信のログは、応答を受け取ったときに1行のまとめだけを出す
                logger?.Log($"[Api] {settings.MethodName} OK");
                return SendAttempt.Success(response, responseHeaders, callContext.ReadResponse);
            }
            catch (RpcException exception) when (
                exception.StatusCode == StatusCode.Unauthenticated && settings.RequiresAuth && canReauthenticate && token != null)
            {
                return SendAttempt.Unauthenticated(token);
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated && settings.RequiresAuth)
            {
                // 弾かれたトークンは、古いセッションの呼び出しでないかを確かめてから呼び出し側(SendWithRetryAsync)が捨てる。
                // トークンを取る途中で弾かれたときは token が null なので、何も捨てない
                return SendAttempt.Failure(ConvertError(settings, exception), token);
            }
            catch (Exception exception)
            {
                return SendAttempt.Failure(ConvertError(settings, exception));
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

        // 成功したレスポンスの後始末。古いセッションのものでなければ、共通レスポンス項目を反映して返す
        private async UniTask<SendOutput> CompleteAsync(
            RpcSettings settings,
            RequestContext context,
            SendAttempt attempt,
            int epoch,
            CallActivity activity)
        {
            // 通信中にタイトル・ホームに戻ったりセッションを捨てたりしていたら、古いセッションのレスポンスなので
            // 反映しない。反映すると、その後にサインインで置き換えた新しいデータを上書きしてしまう
            if (IsStale(epoch))
            {
                logger?.Log($"[Api] {settings.MethodName} discarded after response");
                return SendOutput.Failure(ApiError.Discarded(), ErrorDecision.Stay);
            }

            ApplyResponseHeaders(attempt.ResponseHeaders);

            // 反映処理の例外は、通信のエラーと違って送り直さないので、送信とは別に扱う
            var (applyError, decision) = await ApplyCommonFieldsAsync(settings, context, attempt.ReadResponse, epoch, activity);
            return applyError == null
                ? SendOutput.Success(attempt.Response)
                : SendOutput.Failure(applyError, decision);
        }

        // 共通レスポンス項目を反映する。順番待ちの中で行うので、次の API が送られる時点で必ず反映が終わっている。
        // 反映処理で例外が起きたら CLIENT_ERROR として共通エラーハンドラーに渡し、そのエラーとハンドラーが決めた動きを返す
        private async UniTask<(ApiError Error, ErrorDecision Decision)> ApplyCommonFieldsAsync(
            RpcSettings settings,
            RequestContext context,
            Func<object> readResponse,
            int epoch,
            CallActivity activity)
        {
            if (applyCommonResponse == null)
            {
                return (null, ErrorDecision.Stay);
            }

            try
            {
                await applyCommonResponse(readResponse());
                return (null, ErrorDecision.Stay);
            }
            catch (Exception exception)
            {
                // 反映処理の例外はアプリの実装のバグ。サーバーの処理は成功しているので送り直さない。
                // タイトルに戻ってサインインで全件を取り直し、サーバーとの食い違いを解消してもらう
                logger?.LogException($"[Api] {settings.MethodName} failed to apply the common response of {context.ResponseType.Name}", exception);
                var error = ErrorConverter.FromClientException(exception, errorPolicy);
                var decision = await HandleErrorAsync(error, epoch, activity);
                return (error, decision);
            }
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
                // 遷移は、順番待ちを解放してから SendAsync が行う
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
        private readonly struct SendAttempt
        {
            private SendAttempt(
                SendOutcome outcome,
                ResponseContext response,
                Metadata responseHeaders,
                Func<object> readResponse,
                string token,
                ApiError error)
            {
                Outcome = outcome;
                Response = response;
                ResponseHeaders = responseHeaders;
                ReadResponse = readResponse;
                Token = token;
                Error = error;
            }

            public SendOutcome Outcome { get; }

            // Success のときのレスポンス
            public ResponseContext Response { get; }

            // Success のときのレスポンスヘッダー
            public Metadata ResponseHeaders { get; }

            // Success のときにレスポンスを読む処理
            public Func<object> ReadResponse { get; }

            // Unauthenticated と、再認証できずに UNAUTHENTICATED で失敗したときの、サーバーに弾かれたトークン
            public string Token { get; }

            // Failure のときのエラー
            public ApiError Error { get; }

            public static SendAttempt Success(ResponseContext response, Metadata responseHeaders, Func<object> readResponse)
            {
                return new SendAttempt(SendOutcome.Success, response, responseHeaders, readResponse, null, null);
            }

            public static SendAttempt Unauthenticated(string token)
            {
                return new SendAttempt(SendOutcome.Unauthenticated, null, null, null, token, null);
            }

            public static SendAttempt Failure(ApiError error, string rejectedToken = null)
            {
                return new SendAttempt(SendOutcome.Failure, null, null, null, rejectedToken, error);
            }
        }

        // 順番待ちを抜けた後の結果。エラーハンドラーが決めた画面の遷移も持つ
        private readonly struct SendOutput
        {
            private SendOutput(ResponseContext response, ApiError error, ErrorDecision decision)
            {
                Response = response;
                Error = error;
                Decision = decision;
            }

            public ResponseContext Response { get; }

            public ApiError Error { get; }

            public ErrorDecision Decision { get; }

            public static SendOutput Success(ResponseContext response)
            {
                return new SendOutput(response, null, ErrorDecision.Stay);
            }

            public static SendOutput Failure(ApiError error, ErrorDecision decision)
            {
                return new SendOutput(null, error, decision);
            }
        }
    }
}
