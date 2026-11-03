using System;
using System.Collections.Generic;
using System.Linq;

namespace VolTech.Client
{
    /// <summary>
    /// ApiCore を組み立てる。ApiCore.CreateBuilder で作り、使う部品を With・Add で渡してから Build を呼ぶ。
    /// </summary>
    /// <remarks>
    /// 必須の部品は CreateBuilder の引数で受け取り、任意の部品はメソッドで渡す。渡さなかった部品の処理はしない。
    /// 任意の部品に null を渡すと、渡さなかったのと同じになる(ビルドの種類で付けるかを変えるときに、チェーンを切らずに書ける)。
    /// 同じメソッドを2回呼ぶと後のほうで置き換わる(AddActivityHandler は足していく)。
    /// </remarks>
    public sealed class ApiCoreBuilder
    {
        private readonly List<IApiActivityHandler> activityHandlers = new();

        internal ApiCoreBuilder(
            ApiCoreOptions options,
            ITokenProvider tokenProvider,
            IErrorPolicyProvider errorPolicy,
            ICommonErrorHandler errorHandler)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            TokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            ErrorPolicy = errorPolicy ?? throw new ArgumentNullException(nameof(errorPolicy));
            ErrorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        }

        internal ApiCoreOptions Options { get; }
        internal ITokenProvider TokenProvider { get; }
        internal IErrorPolicyProvider ErrorPolicy { get; }
        internal ICommonErrorHandler ErrorHandler { get; }
        internal IErrorNavigator Navigator { get; private set; }
        internal IApiLogger Logger { get; private set; }
        internal ServerClock ServerClock { get; private set; }
        internal IReadOnlyList<IApiActivityHandler> ActivityHandlers => activityHandlers;
        internal CommonApplier PlayerDiffApplier { get; private set; }
        internal CommonApplier BadgeApplier { get; private set; }
        internal CommonApplier NotificationApplier { get; private set; }

        /// <summary>
        /// 成功したレスポンスの player_diff を反映する処理を渡す。共通レスポンス項目の中で最初に呼ぶ。
        /// </summary>
        /// <param name="applier">player_diff の反映処理。null なら反映しない。</param>
        /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。3 つの反映処理で同じにする。</typeparam>
        /// <returns>このビルダー。</returns>
        public ApiCoreBuilder WithPlayerDiffApplier<TCommon>(IPlayerDiffApplier<TCommon> applier)
            where TCommon : class
        {
            PlayerDiffApplier = applier == null ? null : CommonApplier.Create<TCommon>("player_diff", applier.ApplyAsync);
            return this;
        }

        /// <summary>
        /// 成功したレスポンスのバッジを反映する処理を渡す。player_diff の次に呼ぶ。
        /// </summary>
        /// <param name="applier">バッジの反映処理。null なら反映しない。</param>
        /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。3 つの反映処理で同じにする。</typeparam>
        /// <returns>このビルダー。</returns>
        public ApiCoreBuilder WithBadgeApplier<TCommon>(IBadgeApplier<TCommon> applier)
            where TCommon : class
        {
            BadgeApplier = applier == null ? null : CommonApplier.Create<TCommon>("badges", applier.ApplyAsync);
            return this;
        }

        /// <summary>
        /// 成功したレスポンスの通知を積む処理を渡す。共通レスポンス項目の中で最後に呼ぶ。
        /// </summary>
        /// <param name="applier">通知の反映処理。null なら反映しない。</param>
        /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。3 つの反映処理で同じにする。</typeparam>
        /// <returns>このビルダー。</returns>
        public ApiCoreBuilder WithNotificationApplier<TCommon>(INotificationApplier<TCommon> applier)
            where TCommon : class
        {
            NotificationApplier = applier == null ? null : CommonApplier.Create<TCommon>("notifications", applier.ApplyAsync);
            return this;
        }

        /// <summary>
        /// エラーハンドラーがタイトル・ホーム・ストアへ行くと決めたときの画面遷移を渡す。
        /// 渡さなければ、セッションは終わらせるが画面は遷移しない。
        /// </summary>
        /// <param name="navigator">エラーの後の画面遷移。null なら遷移しない。</param>
        /// <returns>このビルダー。</returns>
        public ApiCoreBuilder WithErrorNavigator(IErrorNavigator navigator)
        {
            Navigator = navigator;
            return this;
        }

        /// <summary>
        /// 通信の結果と例外を出すロガーを渡す。渡さなければログを出さない(ログの文字列も作らない)。
        /// </summary>
        /// <param name="logger">ログの出力先。null ならログを出さない。リリースビルドでは null にする。</param>
        /// <returns>このビルダー。</returns>
        public ApiCoreBuilder WithLogger(IApiLogger logger)
        {
            Logger = logger;
            return this;
        }

        /// <summary>
        /// サーバー時刻を持つ時計を渡す。渡さなければ ApiCore が自分で作る(ApiCore.ServerClock で取れる)。
        /// </summary>
        /// <param name="serverClock">
        /// 成功したレスポンスのサーバー時刻で合わせる時計。ServerClockResetter と同じものを渡すと、前面に戻ったときに合わせ直せる。null なら ApiCore が作る。
        /// </param>
        /// <returns>このビルダー。</returns>
        public ApiCoreBuilder WithServerClock(ServerClock serverClock)
        {
            ServerClock = serverClock;
            return this;
        }

        /// <summary>
        /// 進行中の呼び出しの開始・終了を受け取るハンドラーを足す。何度でも呼べ、開始は足した順、終了は逆の順に呼ぶ。
        /// 1つも足さなければ通知しない。
        /// </summary>
        /// <param name="handler">通信中の表示や入力ブロックを出し入れするハンドラー。null なら足さない。</param>
        /// <returns>このビルダー。</returns>
        public ApiCoreBuilder AddActivityHandler(IApiActivityHandler handler)
        {
            if (handler != null)
            {
                activityHandlers.Add(handler);
            }

            return this;
        }

        /// <summary>
        /// 渡した部品で ApiCore を作る。
        /// </summary>
        /// <returns>作った ApiCore。アプリで1つにする(順番待ちとセッションを持つため)。</returns>
        /// <exception cref="InvalidOperationException">共通レスポンス項目の反映処理の TCommon が食い違っているとき。</exception>
        public ApiCore Build()
        {
            var commonTypes = CommonAppliers().Select(a => a.CommonType).Distinct().ToArray();
            if (commonTypes.Length > 1)
            {
                throw new InvalidOperationException(
                    $"共通レスポンス項目の反映処理の型引数が食い違っている({string.Join(", ", commonTypes.Select(t => t.Name))})。1 つの型にする。");
            }

            return new ApiCore(this);
        }

        // 反映する順(player_diff → バッジ → 通知)に、渡された反映処理だけを返す
        internal IEnumerable<CommonApplier> CommonAppliers()
        {
            if (PlayerDiffApplier != null)
            {
                yield return PlayerDiffApplier;
            }

            if (BadgeApplier != null)
            {
                yield return BadgeApplier;
            }

            if (NotificationApplier != null)
            {
                yield return NotificationApplier;
            }
        }
    }
}
