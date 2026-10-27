using System.Text.Json.Nodes;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// ゲームログ(ガチャでのジェムの消費・キャラクターの入手・編成の変更のような、所持データの変化)の 1 件。
    /// 問い合わせを受けて、失ったアイテムや通貨を戻す(復帰処理)ときの根拠にする。
    /// </summary>
    /// <remarks>
    /// 数量の変化は <see cref="Quantity"/>、数量の変化でないものは <see cref="Event"/> で作り、名前付きの値を <see cref="Field"/> で足す。
    /// player_id・request_id・RPC・時刻は基盤が付ける。
    /// </remarks>
    public sealed class GameLog
    {
        private const int MaxFieldNameLength = 64;

        // 基盤が付ける項目と同じ名前にすると、どちらかが読めなくなる
        private static readonly HashSet<string> reservedFieldNames = new(StringComparer.Ordinal)
        {
            "kind",
            "reason",
            "target_id",
            "delta",
            "after",
            "player_id",
            "request_id",
            "rpc",
        };

        private readonly List<KeyValuePair<string, JsonNode?>> fields = new();

        private GameLog(string kind, string reason, long? delta, long? after)
        {
            Kind = kind;
            Reason = reason;
            Delta = delta;
            After = after;
        }

        /// <summary>種別(例: gems・item・unit)。</summary>
        public string Kind { get; }

        /// <summary>理由(例: gacha_draw)。</summary>
        public string Reason { get; }

        /// <summary>対象の ID(アイテムの ID など)。</summary>
        public string? TargetId { get; private set; }

        /// <summary>増減。数量の変化でなければ null。</summary>
        public long? Delta { get; }

        /// <summary>更新後の値。数量の変化でなければ null。</summary>
        public long? After { get; }

        /// <summary>
        /// 数量の変化を、種別・増減・更新後の値・理由で作る。
        /// </summary>
        public static GameLog Quantity(string kind, long delta, long after, string reason)
        {
            return new GameLog(kind, reason, delta, after);
        }

        /// <summary>
        /// 数量の変化でないもの(キャラクターの入手・編成の変更など)を、種別と理由で作る。
        /// </summary>
        public static GameLog Event(string kind, string reason)
        {
            return new GameLog(kind, reason, null, null);
        }

        /// <summary>
        /// 対象の ID を付ける。名前は変わることがあるので、ID を書く。
        /// </summary>
        public GameLog Target(string targetId)
        {
            TargetId = targetId;
            return this;
        }

        /// <summary>
        /// 名前付きの値(例: unit_id、編成の枠の配列)を足す。名前は英小文字・数字・"_"・"." の 1〜64 文字。
        /// 基盤が付ける項目と同じ名前(kind・player_id など)は使えない。
        /// </summary>
        /// <exception cref="ArgumentException">名前が使えないもの、または同じ名前を 2 回足したとき。</exception>
        public GameLog Field(string name, JsonNode? value)
        {
            ValidateFieldName(name);
            fields.Add(new KeyValuePair<string, JsonNode?>(name, value));
            return this;
        }

        /// <summary>
        /// game_logs.body に入れる JSON。無い値は入れない。
        /// </summary>
        internal JsonObject ToBody()
        {
            var body = new JsonObject();
            if (TargetId != null)
            {
                body["target_id"] = TargetId;
            }

            if (Delta != null)
            {
                body["delta"] = Delta.Value;
                body["after"] = After;
            }

            body["reason"] = Reason;
            foreach (var pair in fields)
            {
                body[pair.Key] = pair.Value?.DeepClone();
            }

            return body;
        }

        private void ValidateFieldName(string name)
        {
            var validCharacters = name.All(character =>
                (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_' || character == '.');
            if (name.Length == 0 || name.Length > MaxFieldNameLength || !validCharacters)
            {
                throw new ArgumentException($"ゲームログの値の名前 \"{name}\" は [a-z0-9_.] の 1〜{MaxFieldNameLength} 文字にする。", nameof(name));
            }

            if (reservedFieldNames.Contains(name))
            {
                throw new ArgumentException($"ゲームログの値の名前 \"{name}\" は基盤が使うので使えない。", nameof(name));
            }

            if (fields.Exists(pair => pair.Key == name))
            {
                throw new ArgumentException($"ゲームログの値の名前 \"{name}\" が重なっている。", nameof(name));
            }
        }
    }
}
