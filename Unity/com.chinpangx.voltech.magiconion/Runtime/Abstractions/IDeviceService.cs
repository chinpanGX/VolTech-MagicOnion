using System;
using MagicOnion;
using MessagePack;

namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// デバイス登録とアクセストークンの発行。認証は要らない。
    /// </summary>
    /// <remarks>
    /// プレイヤーが決まる前に呼ぶので、冪等性の対象にしない(再送しても害はない)。
    /// </remarks>
    [SkipAuth]
    public interface IDeviceService : IService<IDeviceService>
    {
        /// <summary>
        /// クライアントが作った secretKey を登録し、deviceId と playerId を発行する。
        /// playerId は ID だけで、プレイヤー(初期データ)はアプリのサインアップで作る。
        /// </summary>
        /// <param name="secretKey">クライアントが作った秘密の値。16〜128 文字の、表示できる ASCII 文字(0x21〜0x7E)。</param>
        UnaryResult<RegisterResult> RegisterAsync(string secretKey);

        /// <summary>
        /// deviceId と secretKey を確かめ、アクセストークンを発行する。発行し直すと、そのデバイスの古いトークンは無効になる。
        /// </summary>
        UnaryResult<IssueTokenResult> IssueTokenAsync(string deviceId, string secretKey);
    }

    /// <summary><see cref="IDeviceService.RegisterAsync"/> の結果。</summary>
    [MessagePackObject]
    public class RegisterResult
    {
        /// <summary>デバイスの ID(ULID。大文字の英数字 26 文字)。</summary>
        [Key(0)]
        public string DeviceId { get; set; } = "";

        /// <summary>プレイヤーの ID(ULID)。サインアップの前から持てるので、問い合わせなどに表示できる。</summary>
        [Key(1)]
        public string PlayerId { get; set; } = "";
    }

    /// <summary><see cref="IDeviceService.IssueTokenAsync"/> の結果。</summary>
    [MessagePackObject]
    public class IssueTokenResult
    {
        /// <summary>アクセストークン(256 ビットの乱数を 16 進にした、小文字 64 文字)。authorization: Bearer で付ける。</summary>
        [Key(0)]
        public string AccessToken { get; set; } = "";

        /// <summary>有効期限(UTC)。秒の単位に切り捨ててある。</summary>
        [Key(1)]
        public DateTime ExpiresAt { get; set; }
    }
}
