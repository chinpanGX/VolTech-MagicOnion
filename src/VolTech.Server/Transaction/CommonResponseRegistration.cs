using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using VolTech.Server.Abstractions;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// AddCommonResponse で登録した、共通レスポンス項目の型とコレクターの型。DI に 1 つだけ入る。
    /// </summary>
    internal abstract class CommonResponseRegistration
    {
        private static readonly ConcurrentDictionary<Type, bool> implementsApiResponse = new();

        public abstract Type CollectorType { get; }

        /// <summary>リクエストの ServiceProvider からコレクターを作る。</summary>
        public object CreateCollector(IServiceProvider services)
        {
            return ActivatorUtilities.CreateInstance(services, CollectorType);
        }

        /// <summary>レスポンスが登録した TCommon の IApiResponse なら、コレクターから作った Common を入れる。</summary>
        /// <returns>詰めたら true。</returns>
        public abstract ValueTask<bool> FillAsync(object response, TxContext tx);

        /// <summary>
        /// 詰めなかったレスポンスが何かの IApiResponse&lt;&gt; を実装していれば、設定の誤り(登録忘れ・TCommon の食い違い)として例外にする。
        /// </summary>
        public static void ThrowIfUnfilled(object? response, CommonResponseRegistration? registration)
        {
            if (response == null || !ImplementsApiResponse(response.GetType()))
            {
                return;
            }

            throw new InvalidOperationException(registration == null
                ? $"{response.GetType().Name} は IApiResponse<> を実装しているが、AddCommonResponse で共通レスポンス項目を登録していない。"
                : $"{response.GetType().Name} の IApiResponse<> の型引数が、AddCommonResponse で登録した型と違う。");
        }

        /// <summary>型が何かの IApiResponse&lt;&gt; を実装しているか。型ごとに覚える。</summary>
        public static bool ImplementsApiResponse(Type type)
        {
            return implementsApiResponse.GetOrAdd(type, static t => t.GetInterfaces().Any(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IApiResponse<>)));
        }
    }

    internal sealed class CommonResponseRegistration<TCommon, TCollector> : CommonResponseRegistration
        where TCommon : class
        where TCollector : class, ICommonResponseCollector<TCommon>
    {
        public override Type CollectorType => typeof(TCollector);

        public override async ValueTask<bool> FillAsync(object response, TxContext tx)
        {
            if (response is not IApiResponse<TCommon> apiResponse)
            {
                return false;
            }

            apiResponse.Common = await tx.GetCollector<TCollector>().BuildAsync(tx);
            return true;
        }
    }
}
