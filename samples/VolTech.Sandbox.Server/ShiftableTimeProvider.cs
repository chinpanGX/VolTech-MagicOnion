namespace VolTech.Sandbox.Server
{
    /// <summary>
    /// 本当の時刻にずらす量を足して返す時計。動いているサーバーの時刻を変えるために使う。
    /// </summary>
    public sealed class ShiftableTimeProvider : TimeProvider
    {
        private long offsetSeconds;

        /// <summary>本当の時刻からずらしている秒数。</summary>
        public long OffsetSeconds
        {
            get => Interlocked.Read(ref offsetSeconds);
            set => Interlocked.Exchange(ref offsetSeconds, value);
        }

        public override DateTimeOffset GetUtcNow()
        {
            return System.GetUtcNow().AddSeconds(OffsetSeconds);
        }
    }
}
