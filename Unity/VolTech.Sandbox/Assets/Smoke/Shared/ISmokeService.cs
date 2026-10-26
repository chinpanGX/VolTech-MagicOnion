using MagicOnion;
using MessagePack;

namespace VolTech.Sandbox.Smoke
{
    /// <summary>
    /// Unity 7 で MagicOnion のクライアントが動くかを確かめるためのサービス。サーバー側も同じファイルを読み込む。
    /// </summary>
    public interface ISmokeService : IService<ISmokeService>
    {
        UnaryResult<SmokeReply> EchoAsync(string message, int number);
    }

    [MessagePackObject]
    public class SmokeReply
    {
        [Key(0)]
        public string Message { get; set; } = "";

        [Key(1)]
        public int Number { get; set; }

        /// <summary>
        /// サーバーが受け取った x-request-id。呼び出しごとのヘッダーが届いたかを見る。
        /// </summary>
        [Key(2)]
        public string RequestId { get; set; } = "";
    }
}
