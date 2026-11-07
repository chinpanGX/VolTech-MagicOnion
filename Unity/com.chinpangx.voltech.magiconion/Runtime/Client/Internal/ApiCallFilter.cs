using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MagicOnion.Client;

namespace VolTech.Client
{
    /// <summary>
    /// 共有インターフェースの呼び出しを ApiCore に渡すフィルター。ApiCore.CreateClient で、サービスごとに作る。
    /// </summary>
    /// <remarks>
    /// 順番待ちと進行中の数は全サービスで 1 つにするので、処理と状態は ApiCore に置く。ここで持つのは RPC ごとの設定の表だけ。
    /// </remarks>
    internal sealed class ApiCallFilter : IClientFilter
    {
        private readonly ApiCore core;
        private readonly IReadOnlyDictionary<string, RpcSettings> settings;

        public ApiCallFilter(ApiCore core, IReadOnlyDictionary<string, RpcSettings> settings)
        {
            this.core = core;
            this.settings = settings;
        }

        public ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
        {
            // MethodPath は "IGachaService/DrawAsync" の形
            var path = context.MethodPath;
            var method = path.Substring(path.LastIndexOf('/') + 1);
            ApiClientExtensions.ReadCallSettings(context.CallOptions.Headers, out var handledCodes, out var notifyActivity);
            return core.SendAsync(settings[method], context, next, handledCodes, notifyActivity);
        }
    }
}
