using System;

namespace VolTech.Client
{
    /// <summary>
    /// 今のサーバー時刻。端末の時計を使わないので、端末の時刻を変えられても影響を受けない。
    /// </summary>
    public interface IServerClock
    {
        /// <summary>サーバー時刻を一度でも受け取ったか。受け取る前は Now を読めない。</summary>
        public bool IsSynchronized { get; }

        /// <summary>今のサーバー時刻。受け取る前に読むと InvalidOperationException。</summary>
        public DateTimeOffset Now { get; }
    }
}
