namespace VolTech.Server.Auth
{
    /// <summary>
    /// 認証したリクエストの情報。ハンドラーは ServiceContext.GetAuthContext() で取り出す。
    /// </summary>
    public sealed class AuthContext
    {
        public AuthContext(string deviceId, Ulid playerId, bool playerCreated)
        {
            DeviceId = deviceId;
            PlayerId = playerId;
            PlayerCreated = playerCreated;
        }

        /// <summary>デバイスの ID。</summary>
        public string DeviceId { get; }

        /// <summary>デバイス登録で発行したプレイヤーの ID。サインアップの前から必ずある。</summary>
        public Ulid PlayerId { get; }

        /// <summary>サインアップ済み(プレイヤーが作られている)か。</summary>
        public bool PlayerCreated { get; }
    }
}
