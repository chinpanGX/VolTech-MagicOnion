using Microsoft.Extensions.Options;
using VolTech.Server.Hosting;

namespace VolTech.Server.Status
{
    /// <summary>
    /// サーバー時刻と、ゲーム内の日付。
    /// </summary>
    /// <remarks>
    /// ハンドラーは「今日」を、ここではなく ServiceContext.GetGameDate() から取る。
    /// リクエストを受けたときに 1 回だけ決めた日付なので、日付が変わる時刻をまたいでも、処理に使う日付とクライアントに返す日付がずれない。
    /// </remarks>
    public sealed class GameClock
    {
        private readonly TimeProvider timeProvider;
        private readonly IOptionsMonitor<VoltechServerOptions> options;

        public GameClock(TimeProvider timeProvider, IOptionsMonitor<VoltechServerOptions> options)
        {
            this.timeProvider = timeProvider;
            this.options = options;
        }

        /// <summary>今の時刻(UTC)。</summary>
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

        /// <summary>
        /// now のときのゲーム内の日付。
        /// </summary>
        public DateOnly GetGameDate(DateTimeOffset now)
        {
            return GetGameDate(now, options.CurrentValue.GameDay);
        }

        internal static DateOnly GetGameDate(DateTimeOffset now, GameDayOptions gameDay)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(gameDay.TimeZone);
            var local = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
            // 壁時計の時刻から ResetHour 時間を引いた日付にする(夏時間の切り替えの日も、時刻の表示どおりに変わる)
            return DateOnly.FromDateTime(local.AddHours(-gameDay.ResetHour));
        }
    }
}
