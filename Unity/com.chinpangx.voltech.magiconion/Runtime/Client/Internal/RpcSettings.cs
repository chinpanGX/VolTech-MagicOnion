using System;
using System.Collections.Generic;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// RPC ごとの設定。共有インターフェースに付けた属性([SkipAuth]・[NoSideEffects])から決まる。
    /// </summary>
    /// <remarks>
    /// サーバーの RpcSettings(VolTech.Server.Internal)と同じ読み方をする。[SkipAuth] はインターフェースかメソッド、
    /// [NoSideEffects] はメソッドに付ける。冪等性の対象は、どちらも付かない RPC(docs/spec/headers.md)。
    /// </remarks>
    internal sealed class RpcSettings
    {
        internal RpcSettings(string methodName, bool isIdempotencyTarget, bool requiresAuth)
        {
            MethodName = methodName;
            IsIdempotencyTarget = isIdempotencyTarget;
            RequiresAuth = requiresAuth;
        }

        /// <summary>"サービス/メソッド" の形の名前(例: IGachaService/DrawAsync)。ログに出す。</summary>
        internal string MethodName { get; }

        /// <summary>冪等性の対象([NoSideEffects] も [SkipAuth] も付いていない)か。対象なら順番待ちをして、RequestID を付ける。</summary>
        internal bool IsIdempotencyTarget { get; }

        /// <summary>認証が要る([SkipAuth] が付いていない)か。</summary>
        internal bool RequiresAuth { get; }

        /// <summary>
        /// 共有インターフェースのメソッドごとの設定を、メソッドの名前で引ける表にする。
        /// </summary>
        internal static Dictionary<string, RpcSettings> CreateAll(Type serviceType)
        {
            var skipAuthOnService = serviceType.IsDefined(typeof(SkipAuthAttribute), true);
            var all = new Dictionary<string, RpcSettings>();
            foreach (var method in serviceType.GetMethods())
            {
                var skipAuth = skipAuthOnService || method.IsDefined(typeof(SkipAuthAttribute), true);
                var noSideEffects = method.IsDefined(typeof(NoSideEffectsAttribute), true);
                // MagicOnion はメソッドのオーバーロードを許さないので、名前で一意に決まる
                // [SkipAuth] の RPC はプレイヤーが決まらないので、サーバーは冪等性の対象にしない
                all[method.Name] = new RpcSettings(serviceType.Name + "/" + method.Name, !noSideEffects && !skipAuth, !skipAuth);
            }

            return all;
        }
    }
}
