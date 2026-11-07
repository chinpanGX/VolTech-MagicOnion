#nullable enable

using System;
using MagicOnion;
using MessagePack;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.E2E
{
    /// <summary>
    /// Unity のクライアントと本物のサーバーを繋いで確かめるサービス。tests/VolTech.E2E.Server も同じファイルを読み込む。
    /// </summary>
    public interface IE2EService : IService<IE2EService>
    {
        /// <summary>届いたヘッダーを返し、Common を詰める。冪等性の対象。</summary>
        UnaryResult<E2EReply> EchoAsync(string key);

        /// <summary>読み取り専用のトランザクションで動く。</summary>
        [NoSideEffects]
        UnaryResult<E2EReply> ReadAsync(string key);

        /// <summary>認証の要らない RPC。</summary>
        [SkipAuth]
        UnaryResult<E2EReply> PublicAsync(string key);

        /// <summary>1 回目はコミットした後に失敗する(応答が失われたのと同じ)。再送では本体を呼ばない。</summary>
        UnaryResult<E2EReply> LoseResponseOnceAsync(string key);

        /// <summary>業務エラーを返す。</summary>
        UnaryResult<E2EReply> ThrowBusinessAsync();

        /// <summary>予期しない例外を投げる。</summary>
        UnaryResult<E2EReply> ThrowUnexpectedAsync();

        /// <summary>RunAsync を通さずにレスポンスを返す。検査が INTERNAL にする。</summary>
        UnaryResult<E2EReply> WithoutRunAsync();

        /// <summary>このプレイヤーのアクセストークンをサーバーから消す。</summary>
        UnaryResult<bool> ExpireTokenAsync();

        /// <summary>指定の時間だけ待つ。キャンセルされたら止まる。認証は要らない。</summary>
        [SkipAuth]
        UnaryResult<bool> SlowAsync(int milliseconds);

        /// <summary>SlowAsync がキャンセルで止まった回数。</summary>
        [SkipAuth]
        UnaryResult<int> CancelledSlowCallsAsync();
    }

    /// <summary>アプリの共通レスポンス項目の型に当たる。</summary>
    [MessagePackObject]
    public sealed class E2ECommon
    {
        [Key(0)]
        public string Note { get; set; } = "";
    }

    /// <summary>サーバーが見た値を返すレスポンス。</summary>
    [MessagePackObject]
    public sealed class E2EReply : IApiResponse<E2ECommon>
    {
        [Key(0)]
        public E2ECommon? Common { get; set; }

        [Key(1)]
        public string PlayerId { get; set; } = "";

        [Key(2)]
        public string RequestId { get; set; } = "";

        [Key(3)]
        public string GameDate { get; set; } = "";

        [Key(4)]
        public string AppVersion { get; set; } = "";

        /// <summary>同じ key で本体を実行した回数。</summary>
        [Key(5)]
        public int BodyRuns { get; set; }
    }

    /// <summary>E2E のテストの業務エラー。</summary>
    public static class E2EErrors
    {
        public const string Domain = "e2e";

        public const string MetadataKey = "required";

        public static ErrorCode NotEnough => new ErrorCode(Domain, "NOT_ENOUGH");
    }
}
