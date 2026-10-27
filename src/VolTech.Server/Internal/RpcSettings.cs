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
    /// インターフェースマップで実装のメソッドからインターフェースのメソッドを引き、その属性を読む。
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
            var skipAuth = Has<SkipAuthAttribute>(declaring) || Has<SkipAuthAttribute>(interfaceMethod)
                || Has<SkipAuthAttribute>(serviceType) || Has<SkipAuthAttribute>(implementation);
            var noSideEffects = Has<NoSideEffectsAttribute>(interfaceMethod) || Has<NoSideEffectsAttribute>(implementation);
            return new RpcSettings(fullName, skipAuth, noSideEffects);
        }

        private static MethodInfo? FindInterfaceMethod(Type serviceType, MethodInfo implementation)
        {
            foreach (var serviceInterface in serviceType.GetInterfaces())
            {
                var map = serviceType.GetInterfaceMap(serviceInterface);
                for (var i = 0; i < map.TargetMethods.Length; i++)
                {
                    if (map.TargetMethods[i] == implementation)
                    {
                        return map.InterfaceMethods[i];
                    }
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
