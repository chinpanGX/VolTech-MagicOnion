using MagicOnion.Server;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// ハンドラーの本体をトランザクションの中で動かし、冪等性の対象の RPC では二重実行を防ぐ。
    /// </summary>
    public interface ITransactions
    {
        /// <summary>
        /// handler をトランザクションの中で動かし、成功したらコミットする。例外で抜けたらロールバックする。
        /// </summary>
        /// <remarks>
        /// RPC の属性によって動きが変わる。
        /// <list type="bullet">
        /// <item>[NoSideEffects]: 読み取り専用のトランザクションで動かす。ゲームログは書けない</item>
        /// <item>[SkipAuth]: 普通のトランザクションで動かす。プレイヤーが決まらないので、冪等性の対象にしない</item>
        /// <item>それ以外(冪等性の対象): プレイヤーの直前のレスポンスの行をロックし、同じ x-request-id なら handler を呼ばずに
        /// 保存済みのレスポンスを返す。成功したらレスポンスを保存する。x-request-id が無い・形が違うときは INVALID_ARGUMENT</item>
        /// </list>
        /// </remarks>
        /// <param name="context">サービスの Context。</param>
        /// <param name="handler">本体。レスポンスを返す。</param>
        Task<TResponse> RunAsync<TResponse>(ServiceContext context, Func<TxContext, Task<TResponse>> handler);
    }
}
