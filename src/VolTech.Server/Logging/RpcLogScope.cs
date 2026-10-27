using System.Collections;
using MagicOnion.Server;
using VolTech.Server.Internal;

namespace VolTech.Server.Logging
{
    /// <summary>
    /// RPC の間に出すログに付ける値(ILogger のスコープ)。player_id と replayed は、決まったときに後から書き込む。
    /// </summary>
    /// <remarks>
    /// authorization などのヘッダーは入れない。入れるのは x-request-id(形が正しいときだけ)と、RPC の名前だけ。
    /// </remarks>
    internal sealed class RpcLogScope : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public RpcLogScope(string rpcMethod, string? requestId)
        {
            RpcMethod = rpcMethod;
            RequestId = requestId;
        }

        public string RpcMethod { get; }

        public string? RequestId { get; }

        public string? PlayerId { get; set; }

        /// <summary>再送で保存済みのレスポンスを返したか。冪等性の対象の RPC でだけ決まる。</summary>
        public bool? Replayed { get; set; }

        public int Count => 4;

        /// <summary>RpcLogFilter が ServiceContext に入れたスコープ。フィルターを通っていなければ null。</summary>
        public static RpcLogScope? Find(ServiceContext context)
        {
            return context.Items.TryGetValue(ContextItems.RpcLog, out var value) ? value as RpcLogScope : null;
        }

        public void SetPlayerId(Ulid playerId)
        {
            PlayerId = playerId.ToString();
        }

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                return index switch
                {
                    0 => new KeyValuePair<string, object?>("rpc.method", RpcMethod),
                    1 => new KeyValuePair<string, object?>("request_id", RequestId),
                    2 => new KeyValuePair<string, object?>("player_id", PlayerId),
                    3 => new KeyValuePair<string, object?>("replayed", Replayed),
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                };
            }
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return $"rpc.method={RpcMethod} request_id={RequestId} player_id={PlayerId} replayed={Replayed}";
        }
    }
}
