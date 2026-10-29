namespace VolTech.Client.Auth
{
    /// <summary>
    /// 端末に保存するアカウント情報。
    /// </summary>
    public sealed class AccountData
    {
        /// <summary>
        /// デバイス登録で受け取った device_id・player_id と、登録に使った secret_key を受け取って作る。
        /// </summary>
        public AccountData(string deviceId, string secretKey, string playerId)
        {
            DeviceId = deviceId;
            SecretKey = secretKey;
            PlayerId = playerId;
        }

        /// <summary>デバイス登録で受け取った device_id。</summary>
        public string DeviceId { get; }

        /// <summary>デバイス登録に使った secret_key。</summary>
        public string SecretKey { get; }

        /// <summary>
        /// デバイス登録で受け取った player_id(ULID)。サインアップの前から持つので、問い合わせなどに表示できる。
        /// サインアップ済みかどうかは表さない(サインインの結果で判断する)。
        /// </summary>
        public string PlayerId { get; }
    }
}
