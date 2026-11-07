using Grpc.Core;
using MagicOnion;
using MagicOnion.Server;
using VolTech.Sandbox.Sample;
using VolTech.Server.Status;

namespace VolTech.Sandbox.Server
{
    /// <summary>
    /// サーバーの時刻を変える開発用のサービス。認証なしで時刻を変えられるので、本番のサーバーに写さない。
    /// </summary>
    public sealed class ServerClockService : ServiceBase<IServerClockService>, IServerClockService
    {
        private const long MaxOffsetSeconds = 3650L * 24 * 60 * 60;

        private readonly ShiftableTimeProvider timeProvider;
        private readonly GameClock clock;

        public ServerClockService(ShiftableTimeProvider timeProvider, GameClock clock)
        {
            this.timeProvider = timeProvider;
            this.clock = clock;
        }

        public UnaryResult<ServerClockInfo> GetAsync()
        {
            return UnaryResult.FromResult(CreateInfo());
        }

        public UnaryResult<ServerClockInfo> SetOffsetAsync(long seconds)
        {
            if (seconds < 0 || seconds > MaxOffsetSeconds)
            {
                throw new ReturnStatusException(StatusCode.InvalidArgument, "offset out of range");
            }

            timeProvider.OffsetSeconds = seconds;
            return UnaryResult.FromResult(CreateInfo());
        }

        // Context.GetGameDate() はリクエストを受けたときの日付で、変更前の値になるので、GameClock で作る
        private ServerClockInfo CreateInfo()
        {
            var now = clock.UtcNow;
            return new ServerClockInfo
            {
                UtcNow = now.UtcDateTime,
                GameDate = clock.GetGameDate(now).ToString("yyyy-MM-dd"),
                OffsetSeconds = timeProvider.OffsetSeconds,
            };
        }
    }
}
