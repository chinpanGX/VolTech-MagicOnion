using System.Collections.Concurrent;
using System.Reflection;
using MagicOnion.Server;
using VolTech.Server.Abstractions;

namespace VolTech.Server.Internal
{
    /// <summary>
    /// RPC ごとの設定。共有インターフェースに付けた属性([SkipAuth]・[NoSideEffects])から決まる。
    /// </summary>
    /// <remarks>
    /// ServiceContext.MethodInfo と AttributeLookup は実装クラスの情報なので、インターフェースの属性が見えない。
    /// インターフェースマップで実装のメソッドから共有インターフェース(ServiceBase&lt;T&gt; の T)のメソッドを引き、その属性を読む。
    /// 実装クラスと実装メソッドの属性は読まない。クライアントは共有インターフェースしか知らないので、読むと設定が食い違う。
    /// </remarks>
    internal sealed class RpcSettings
    {
        private static readonly ConcurrentDictionary<MethodInfo, RpcSettings> cache = new();

        private RpcSettings(string fullName, bool skipAuth, bool noSideEffects)
        {
            FullName = fullName;
            SkipAuth = skipAuth;
            NoSideEffects = noSideEffects;
        }

        /// <summary>"サービス/メソッド" の形の名前(例: IGachaService/DrawAsync)。ログと保存済みレスポンスに使う。</summary>
        public string FullName { get; }

        /// <summary>認証が要らないか。</summary>
        public bool SkipAuth { get; }

        /// <summary>副作用が無い(読み取り専用の)RPC か。付いていなければ冪等性の対象。</summary>
        public bool NoSideEffects { get; }

        public static RpcSettings Get(ServiceContext context)
        {
            return cache.GetOrAdd(
                context.MethodInfo,
                static (method, context) => Create(context.ServiceType, method, context.ServiceName + "/" + context.MethodName),
                context);
        }

        internal static RpcSettings Create(Type serviceType, MethodInfo implementation, string fullName)
        {
            var interfaceMethod = FindInterfaceMethod(serviceType, implementation);
            var declaring = interfaceMethod?.DeclaringType;
            var skipAuth = Has<SkipAuthAttribute>(declaring) || Has<SkipAuthAttribute>(interfaceMethod);
            var noSideEffects = Has<NoSideEffectsAttribute>(interfaceMethod);
            return new RpcSettings(fullName, skipAuth, noSideEffects);
        }

        // 共有インターフェースだけを見る。実装クラスのほかのインターフェースに同じシグネチャのメソッドがあっても、その属性は読まない。
        // 見つからなければ null(属性が無いものとして、認証が要る・冪等性の対象になる)
        private static MethodInfo? FindInterfaceMethod(Type serviceType, MethodInfo implementation)
        {
            var serviceInterface = FindServiceInterface(serviceType);
            if (serviceInterface == null)
            {
                return null;
            }

            var map = serviceType.GetInterfaceMap(serviceInterface);
            for (var i = 0; i < map.TargetMethods.Length; i++)
            {
                if (map.TargetMethods[i] == implementation)
                {
                    return map.InterfaceMethods[i];
                }
            }

            return null;
        }

        // MagicOnion がサービスとして登録するのは ServiceBase<T> の T だけ(docs/design/server.md)
        private static Type? FindServiceInterface(Type serviceType)
        {
            for (var type = serviceType; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ServiceBase<>))
                {
                    return type.GetGenericArguments()[0];
                }
            }

            return null;
        }

        private static bool Has<T>(MemberInfo? member)
            where T : Attribute
        {
            return member != null && member.IsDefined(typeof(T), inherit: true);
        }
    }
}
