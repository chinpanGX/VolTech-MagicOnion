using System;
using Cysharp.Threading.Tasks;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 共通レスポンス項目の反映処理 1 つを、TCommon を知らない ApiCore から呼べる形にしたもの。
    /// </summary>
    internal sealed class CommonApplier
    {
        private readonly Func<object, object> extract;
        private readonly Func<object, UniTask> apply;

        private CommonApplier(string name, Type commonType, Func<object, object> extract, Func<object, UniTask> apply)
        {
            Name = name;
            CommonType = commonType;
            this.extract = extract;
            this.apply = apply;
        }

        /// <summary>ログに出す名前(player_diff・badges・notifications)。</summary>
        public string Name { get; }

        /// <summary>反映処理の TCommon。</summary>
        public Type CommonType { get; }

        public static CommonApplier Create<TCommon>(string name, Func<TCommon, UniTask> apply)
            where TCommon : class
        {
            return new CommonApplier(
                name,
                typeof(TCommon),
                static response => response is IApiResponse<TCommon> apiResponse ? apiResponse.Common : null,
                common => apply((TCommon)common));
        }

        /// <summary>レスポンスが TCommon の IApiResponse なら Common を返す。違う型か、Common が null なら null。</summary>
        public object Extract(object response)
        {
            return response == null ? null : extract(response);
        }

        public UniTask ApplyAsync(object common)
        {
            return apply(common);
        }
    }
}
