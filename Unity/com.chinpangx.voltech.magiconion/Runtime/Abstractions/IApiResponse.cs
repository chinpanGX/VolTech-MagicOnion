#nullable enable

namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// 共通レスポンス項目(player_diff・バッジ・通知)を返すレスポンスの型が実装する。
    /// </summary>
    /// <remarks>
    /// TCommon はアプリが決める 1 つの型で、player_diff・バッジ・通知をまとめて持つ。基盤は中身を知らない。
    /// サーバーでは基盤(ITransactions.RunAsync)が Common を入れ、クライアントでは ApiCore が Common を反映処理に渡す。
    /// MessagePack の型では、Common にも [Key] を付ける。共通レスポンス項目を使わないレスポンスには実装しない。
    /// </remarks>
    /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。</typeparam>
    public interface IApiResponse<TCommon> where TCommon : class
    {
        /// <summary>共通レスポンス項目。null なら変化なし。</summary>
        TCommon? Common { get; set; }
    }
}
