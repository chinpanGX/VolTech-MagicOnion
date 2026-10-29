using System;
using NUnit.Framework;
using VolTech.Client;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// ServerClock が、受け取ったサーバー時刻と経過時間から今のサーバー時刻を計算することを確かめる。
    /// </summary>
    [Description("ServerClock が、受け取ったサーバー時刻と経過時間から今のサーバー時刻を計算することを確かめる")]
    public class ServerClockTests
    {
        // static フィールドは持たない(Unity 7 は static フィールドに属性を求める。UAL0010)
        private readonly DateTimeOffset serverTime = new(2026, 10, 6, 3, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// サーバー時刻を受け取る前は同期していないとされ、Now を読むと例外になる。
        /// </summary>
        [Test]
        [Description("サーバー時刻を受け取る前は同期していないとされ、Now を読むと例外になる")]
        public void NowThrowsBeforeSynchronize()
        {
            var clock = new ServerClock(() => TimeSpan.Zero);

            Assert.That(clock.IsSynchronized, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = clock.Now);
        }

        /// <summary>
        /// 受け取ったサーバー時刻に、受け取ってからの経過時間を足した時刻を返す。
        /// </summary>
        [Test]
        [Description("受け取ったサーバー時刻に、受け取ってからの経過時間を足した時刻を返す")]
        public void NowAddsElapsedSinceSynchronize()
        {
            var elapsed = TimeSpan.FromSeconds(100);
            var clock = new ServerClock(() => elapsed);

            clock.Synchronize(serverTime);
            elapsed += TimeSpan.FromSeconds(30);

            Assert.That(clock.IsSynchronized, Is.True);
            Assert.That(clock.Now, Is.EqualTo(serverTime.AddSeconds(30)));
        }

        /// <summary>
        /// Reset すると受け取る前の状態に戻り、次に受け取ったサーバー時刻で合わせ直す(スリープから戻ったとき)。
        /// </summary>
        [Test]
        [Description("Reset すると受け取る前の状態に戻り、次に受け取ったサーバー時刻で合わせ直す(スリープから戻ったとき)")]
        public void ResetUntilNextSynchronize()
        {
            var elapsed = TimeSpan.Zero;
            var clock = new ServerClock(() => elapsed);
            clock.Synchronize(serverTime);

            clock.Reset();
            Assert.That(clock.IsSynchronized, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = clock.Now);

            elapsed = TimeSpan.FromSeconds(1);
            clock.Synchronize(serverTime.AddHours(1));
            Assert.That(clock.Now, Is.EqualTo(serverTime.AddHours(1)));
        }

        /// <summary>
        /// もう一度受け取ると、新しいサーバー時刻を基準にし直す。
        /// </summary>
        [Test]
        [Description("もう一度受け取ると、新しいサーバー時刻を基準にし直す")]
        public void SynchronizeAgainReplacesBase()
        {
            var elapsed = TimeSpan.Zero;
            var clock = new ServerClock(() => elapsed);
            clock.Synchronize(serverTime);

            elapsed = TimeSpan.FromSeconds(10);
            clock.Synchronize(serverTime.AddSeconds(15));
            elapsed = TimeSpan.FromSeconds(12);

            Assert.That(clock.Now, Is.EqualTo(serverTime.AddSeconds(17)));
        }
    }
}
