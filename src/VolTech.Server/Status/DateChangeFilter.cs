using MagicOnion.Server;
using MagicOnion.Server.Filters;
using VolTech.Server.Abstractions;
using VolTech.Server.Internal;

namespace VolTech.Server.Status
{
    /// <summary>
    /// リクエストの x-game-date が今のゲーム内の日付と違えば DATE_CHANGED を返す。AuthFilter の内側に置く。
    /// </summary>
    /// <remarks>
    /// ヘッダーが無ければ判定しないので、トークンの発行とサインインは引っかからない。[SkipAuth] の RPC も判定しない。
    /// </remarks>
    internal sealed class DateChangeFilter : IMagicOnionServiceFilter
    {
        public ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
        {
            if (RpcSettings.Get(context).SkipAuth)
            {
                return next(context);
            }

            var requested = context.CallContext.RequestHeaders.GetValue(ApiHeaders.GameDate);
            if (requested != null && requested != ServerTimeFilter.FormatDate(context.GetGameDate()))
            {
                throw new BusinessException(ReservedErrors.DateChanged, "game date changed");
            }

            return next(context);
        }
    }
}
