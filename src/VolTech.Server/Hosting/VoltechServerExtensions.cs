using MagicOnion.Server;
using MagicOnion.Server.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VolTech.Server.Auth;
using VolTech.Server.Logging;
using VolTech.Server.Status;
using VolTech.Server.Transaction;

namespace VolTech.Server.Hosting
{
    /// <summary>
    /// 基盤をアプリのサーバーに登録する。
    /// </summary>
    public static class VoltechServerExtensions
    {
        /// <summary>
        /// 基盤のフィルターの Order。アプリがクラスやメソッドに付けたフィルター(Order の既定は int.MaxValue)より外側に来るよう、
        /// int.MinValue の近くにする。小さいほど外側。
        /// </summary>
        public static class FilterOrder
        {
            public const int RpcLog = int.MinValue;
            public const int ServerTime = int.MinValue + 1;
            public const int Auth = int.MinValue + 2;
            public const int DateChange = int.MinValue + 3;
        }

        /// <summary>
        /// gRPC・MagicOnion と、基盤のフィルター・サービスを登録する。フィルターの順序はここで固定する。
        /// </summary>
        /// <remarks>
        /// アプリは別に次を登録する。
        /// <list type="bullet">
        /// <item>NpgsqlDataSource(DB への接続。1 つだけ)</item>
        /// <item><see cref="IPlayerResolver"/> の実装</item>
        /// </list>
        /// サーバーは HTTP/2 で待ち受ける。TLS なし(h2c)で動かすときは、Kestrel の Protocols を Http2 にする。
        /// </remarks>
        public static IMagicOnionServerBuilder AddVoltechServer(
            this IServiceCollection services,
            Action<VoltechServerOptions>? configure = null)
        {
            var options = services.AddOptions<VoltechServerOptions>();
            if (configure != null)
            {
                options.Configure(configure);
            }

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<VoltechServerOptions>, VoltechServerOptionsValidator>());
            options.ValidateOnStart();

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<GameClock>();
            services.TryAddSingleton<ITransactions, Transactions>();

            services.AddGrpc();
            return services.AddMagicOnion(magicOnion =>
            {
                magicOnion.GlobalFilters.Add(new MagicOnionServiceFilterDescriptor(typeof(RpcLogFilter), FilterOrder.RpcLog));
                magicOnion.GlobalFilters.Add(new MagicOnionServiceFilterDescriptor(typeof(ServerTimeFilter), FilterOrder.ServerTime));
                magicOnion.GlobalFilters.Add(new MagicOnionServiceFilterDescriptor(typeof(AuthFilter), FilterOrder.Auth));
                magicOnion.GlobalFilters.Add(new MagicOnionServiceFilterDescriptor(typeof(DateChangeFilter), FilterOrder.DateChange));
            });
        }
    }
}
