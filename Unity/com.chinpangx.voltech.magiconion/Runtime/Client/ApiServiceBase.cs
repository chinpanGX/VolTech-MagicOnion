using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MagicOnion;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// アプリが書くラッパー(GachaApi など)の基底クラス。ラッパーは、ここの InvokeAsync を通して ApiCore で RPC を呼ぶ。
    /// </summary>
    /// <remarks>
    /// RPC ごとの設定(認証が要るか・冪等性の対象か)は、共有インターフェース TService の属性([SkipAuth]・[NoSideEffects])から読む。
    /// サーバーも同じ属性を読むので、食い違わない。ApiCore.InvokeAsync は internal のまま、派生クラスにだけ protected で見せる。
    /// </remarks>
    /// <typeparam name="TService">MagicOnion の共有インターフェース(IService を継承したもの)。</typeparam>
    public abstract class ApiServiceBase<TService>
        where TService : IService<TService>
    {
        private readonly ApiCore core;
        private readonly TService client;
        private readonly Dictionary<string, RpcSettings> settings;

        /// <summary>
        /// RPC を呼ぶのに使う ApiCore と、MagicOnion のクライアントを受け取って作る。
        /// </summary>
        /// <param name="core">共通処理。アプリで1つの ApiCore を、すべてのラッパーに渡す。</param>
        /// <param name="client">MagicOnionClient.Create で作った TService のクライアント。</param>
        /// <exception cref="ArgumentNullException">core か client が null のとき。</exception>
        protected ApiServiceBase(ApiCore core, TService client)
        {
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            this.client = client;
            settings = RpcSettings.CreateAll(typeof(TService));
        }

        /// <summary>
        /// ApiCore で RPC を呼ぶ。失敗したときはエラーハンドラーの処理を済ませてから、失敗の結果を返す。
        /// </summary>
        /// <param name="methodName">TService のメソッドの名前。nameof で渡す。</param>
        /// <param name="request">RPC の引数。引数が複数ならタプルにまとめる。</param>
        /// <param name="call">RPC を呼ぶ処理。キャプチャしない static ラムダで渡す(例: static (c, r) =&gt; c.DrawAsync(r))。</param>
        /// <param name="handledCodes">呼び出し側が自分で処理するエラーコード。ダイアログを出さずに失敗として返る。</param>
        /// <param name="notifyActivity">
        /// 進行中の呼び出しとしてアクティビティのハンドラーに知らせるか。画面を止めずに裏で呼ぶものは false にする。
        /// </param>
        /// <returns>呼び出しの結果。</returns>
        /// <exception cref="ArgumentException">methodName が TService のメソッドでないとき。</exception>
        protected UniTask<ApiResult<TResponse>> InvokeAsync<TRequest, TResponse>(
            string methodName,
            TRequest request,
            Func<TService, TRequest, UnaryResult<TResponse>> call,
            IEnumerable<ErrorCode> handledCodes = null,
            bool notifyActivity = true)
        {
            if (methodName == null || !settings.TryGetValue(methodName, out var rpcSettings))
            {
                throw new ArgumentException($"{typeof(TService).Name} に {methodName} というメソッドが無い。", nameof(methodName));
            }

            return core.InvokeAsync(rpcSettings, client, request, handledCodes, call, notifyActivity);
        }
    }
}
