using VolTech.Server.Abstractions;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// 共通レスポンス項目(player_diff・バッジ・通知)をリクエストの間に集め、レスポンスに詰める TCommon を作る。アプリが実装する。
    /// </summary>
    /// <remarks>
    /// トランザクションごとに 1 つ作られる(<see cref="TxContext.GetCollector{TCollector}"/>)。コンストラクターで DI の部品を受け取れる。
    /// 書き込み関数などが記録し、本体が成功したら基盤が同じトランザクションの中で <see cref="BuildAsync"/> を呼び、
    /// レスポンスの <see cref="IApiResponse{TCommon}.Common"/> に入れる。ロールバックしたら中身ごと捨てられる。
    /// 登録は AddCommonResponse で行う。
    /// </remarks>
    /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。</typeparam>
    public interface ICommonResponseCollector<TCommon> where TCommon : class
    {
        /// <summary>
        /// 記録した中身から共通レスポンス項目を作る。変化が無ければ null を返してよい。
        /// </summary>
        /// <param name="tx">本体と同じトランザクション。バッジの計算などで読める。</param>
        ValueTask<TCommon?> BuildAsync(TxContext tx);
    }
}
