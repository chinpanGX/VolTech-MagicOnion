using System;
using System.Collections.Generic;

namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// 業務エラー。ハンドラーやフィルターが投げると、最も外側のフィルターが FAILED_PRECONDITION と
    /// google.rpc.ErrorInfo のトレーラー(grpc-status-details-bin)に変換してクライアントへ返す。
    /// </summary>
    public class BusinessException : Exception
    {
        private static readonly IReadOnlyDictionary<string, string> emptyMetadata = new Dictionary<string, string>();

        /// <summary>
        /// エラーコードとメッセージ(と表示用の値)から作る。
        /// </summary>
        /// <param name="code">エラーコード。</param>
        /// <param name="message">メッセージ。クライアントの表示には使わない(表示は文言キーで決まる)。ログとデバッグ用。</param>
        /// <param name="metadata">表示用の値(ErrorInfo.metadata)。キー "action" は予約されているので使えない。渡した内容は写して持つ。</param>
        /// <exception cref="ArgumentException">metadata にキー "action" があるとき。</exception>
        public BusinessException(ErrorCode code, string message, IReadOnlyDictionary<string, string>? metadata = null)
            : base(message)
        {
            Code = code;

            if (metadata == null)
            {
                Metadata = emptyMetadata;
                return;
            }

            if (metadata.ContainsKey(ErrorMetadataKeys.Action))
            {
                throw new ArgumentException(
                    $"metadata のキー \"{ErrorMetadataKeys.Action}\" は action の上書きに使うので、業務エラーの値には使えない。",
                    nameof(metadata));
            }

            var copy = new Dictionary<string, string>();
            foreach (var pair in metadata)
            {
                copy[pair.Key] = pair.Value;
            }

            Metadata = copy;
        }

        /// <summary>エラーコード。</summary>
        public ErrorCode Code { get; }

        /// <summary>表示用の値(ErrorInfo.metadata)。無ければ空。</summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
