namespace VolTech.Server.Hosting
{
    /// <summary>
    /// 基盤の設定。AddVoltechServer の引数か、設定ファイル(例: セクション "Voltech")から入れる。
    /// </summary>
    public sealed class VoltechServerOptions
    {
        /// <summary>ゲーム内の日付の決め方。</summary>
        public GameDayOptions GameDay { get; set; } = new GameDayOptions();

        /// <summary>発行するアクセストークンの有効期限。既定は 1 時間。</summary>
        public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    }

    /// <summary>
    /// ゲーム内の日付の決め方。TimeZone の時刻で ResetHour 時に日付が変わる。
    /// </summary>
    public sealed class GameDayOptions
    {
        /// <summary>日付が変わる時(0〜23)。</summary>
        public int ResetHour { get; set; }

        /// <summary>日付を数えるタイムゾーン(IANA の名前。例: Asia/Tokyo)。</summary>
        public string TimeZone { get; set; } = "UTC";
    }
}
