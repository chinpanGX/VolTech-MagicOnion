using System;
using System.Diagnostics;

namespace VolTech.Client
{
    /// <summary>
    /// 最後に受け取ったサーバー時刻と、そこからの経過時間で、今のサーバー時刻を計算する。
    /// </summary>
    /// <remarks>
    /// 経過時間は、端末の時刻の変更に影響されない時計(既定は Stopwatch)で測る。
    /// Unity の realtimeSinceStartup はメインスレッドからしか読めず、通信の後の処理がメインスレッドで動くとは限らないので使わない。
    /// ただし iOS・Android の Stopwatch は端末のスリープ中に止まるので、スリープの後は実際より遅れる。
    /// アプリが前面に戻ったら ServerClockResetter が Reset を呼び、次に受け取るサーバー時刻で合わせ直す。
    /// </remarks>
    public sealed class ServerClock : IServerClock
    {
        private readonly Func<TimeSpan> elapsed;
        private readonly object gate = new();

        private bool synchronized;
        private DateTimeOffset syncedServerTime;
        private TimeSpan syncedElapsed;

        /// <summary>
        /// 経過時間を返す処理を受け取って作る。
        /// </summary>
        /// <param name="elapsed">単調に増える経過時間を返す処理。テストで差し替える。省略すると Stopwatch。</param>
        public ServerClock(Func<TimeSpan> elapsed = null)
        {
            if (elapsed == null)
            {
                var stopwatch = Stopwatch.StartNew();
                elapsed = () => stopwatch.Elapsed;
            }

            this.elapsed = elapsed;
        }

        /// <inheritdoc />
        public bool IsSynchronized
        {
            get
            {
                lock (gate)
                {
                    return synchronized;
                }
            }
        }

        /// <inheritdoc />
        public DateTimeOffset Now
        {
            get
            {
                lock (gate)
                {
                    if (!synchronized)
                    {
                        throw new InvalidOperationException("server time has not been received yet");
                    }

                    return syncedServerTime + (elapsed() - syncedElapsed);
                }
            }
        }

        /// <summary>
        /// 受け取る前の状態に戻す。次に Synchronize するまで、IsSynchronized は false になり、Now は読めない。
        /// </summary>
        internal void Reset()
        {
            lock (gate)
            {
                synchronized = false;
            }
        }

        /// <summary>
        /// 受け取ったサーバー時刻を記録する。
        /// </summary>
        internal void Synchronize(DateTimeOffset serverTime)
        {
            lock (gate)
            {
                syncedServerTime = serverTime;
                syncedElapsed = elapsed();
                synchronized = true;
            }
        }
    }
}
