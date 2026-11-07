using System.Collections.Generic;
using System.Threading;
using Unity.Scripting.LifecycleManagement;

namespace VolTech.Client.Editor
{
    /// <summary>
    /// 通信の記録の置き場所。古いものから順に、最大 Capacity 件を持つ。
    /// </summary>
    /// <remarks>
    /// Interceptor の続きはメインスレッドで動かないので、追加・置き換えは lock で守る。
    /// 読む側(ウィンドウ)は Version を見て、変わっていたら Snapshot を取り直す。
    /// Play を止めても見返せるように、static の Shared は Play の区切りで消さない。
    /// </remarks>
    [NoAutoStaticsCleanup]
    internal sealed class ApiTraceStore
    {
        /// <summary>持つ件数の上限。超えたら古いものから捨てる。</summary>
        public const int Capacity = 500;

        private readonly object gate = new();
        private readonly List<ApiTrace> traces = new();
        private int version;
        private int lastId;

        /// <summary>Editor の通信の記録。ApiChannel.CreateCallInvoker で作った通信が入る。</summary>
        public static ApiTraceStore Shared { get; } = new();

        /// <summary>中身が変わるたびに増える版。</summary>
        public int Version => Volatile.Read(ref version);

        /// <summary>記録するか。false の間は、Interceptor が記録しない(ウィンドウの一時停止)。</summary>
        public bool Recording { get; set; } = true;

        /// <summary>
        /// 新しい記録の連番を返す。
        /// </summary>
        public int NextId()
        {
            return Interlocked.Increment(ref lastId);
        }

        /// <summary>
        /// 新しい記録を末尾に足す。Capacity を超えたら古いものから捨てる。
        /// </summary>
        public void Add(ApiTrace trace)
        {
            lock (gate)
            {
                traces.Add(trace);
                if (traces.Count > Capacity)
                {
                    traces.RemoveAt(0);
                }

                version++;
            }
        }

        /// <summary>
        /// 同じ Id の記録を置き換える。その記録がもう無い(Clear や Capacity 超えで捨てた)ときは何もせず、false を返す。
        /// </summary>
        /// <remarks>
        /// 送信中に消した記録が、応答を受け取ったときに戻ってこないようにする。
        /// </remarks>
        public bool Update(ApiTrace trace)
        {
            lock (gate)
            {
                // 置き換えるのは、たいてい最近の記録なので、後ろから探す
                for (var index = traces.Count - 1; index >= 0; index--)
                {
                    if (traces[index].Id == trace.Id)
                    {
                        traces[index] = trace;
                        version++;
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 今の記録を、古い順に返す(複製した一覧なので、後から中身が変わらない)。
        /// </summary>
        public ApiTrace[] Snapshot()
        {
            lock (gate)
            {
                return traces.ToArray();
            }
        }

        /// <summary>
        /// 記録をすべて捨てる。
        /// </summary>
        public void Clear()
        {
            lock (gate)
            {
                traces.Clear();
                version++;
            }
        }
    }
}
