using System.Collections.Generic;

namespace VolTech.Client
{
    /// <summary>
    /// サーバーがエラーと一緒に返す google.rpc.ErrorInfo の中身。
    /// </summary>
    internal sealed class RpcErrorInfo
    {
        /// <summary>
        /// ErrorInfo の中身を受け取って作る。
        /// </summary>
        internal RpcErrorInfo(string reason, string domain, IReadOnlyDictionary<string, string> metadata)
        {
            Reason = reason ?? "";
            Domain = domain ?? "";
            Metadata = metadata ?? new Dictionary<string, string>();
        }

        /// <summary>ErrorInfo.reason(例: PLAYER_NOT_FOUND)。</summary>
        internal string Reason { get; }

        /// <summary>ErrorInfo.domain(例: voltech)。</summary>
        internal string Domain { get; }

        /// <summary>ErrorInfo.metadata。無ければ空。</summary>
        internal IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
