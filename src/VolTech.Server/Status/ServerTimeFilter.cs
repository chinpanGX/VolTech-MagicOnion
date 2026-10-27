using System.Globalization;
using Grpc.Core;
using MagicOnion.Server;
using MagicOnion.Server.Filters;
using VolTech.Server.Abstractions;
using VolTech.Server.Internal;

namespace VolTech.Server.Status
{
    /// <summary>
    /// レスポンスヘッダーに x-server-time と x-game-date を付ける。DB は使わない。
    /// </summary>
    /// <remarks>
    /// ヘッダーは本文より前に送るので、next を呼ぶ前に書く。これで業務エラーのレスポンスにもヘッダーが付く。
    /// 強制アップデートとメンテナンスの判定を足すときは、このフィルターに入れる。
    /// </remarks>
    internal sealed class ServerTimeFilter : IMagicOnionServiceFilter
    {
        private readonly GameClock clock;

        public ServerTimeFilter(GameClock clock)
        {
            this.clock = clock;
        }

        public async ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
        {
            var now = clock.UtcNow;
            // ハンドラーとレスポンスヘッダーで同じ日付を使うように、受け取ったときに 1 回だけ決める
            var today = clock.GetGameDate(now);
            context.Items[ContextItems.GameDate] = today;

            var headers = new Metadata
            {
                { ApiHeaders.ServerTime, now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) },
                { ApiHeaders.GameDate, FormatDate(today) },
            };
            await context.CallContext.WriteResponseHeadersAsync(headers);
            await next(context);
        }

        internal static string FormatDate(DateOnly date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
