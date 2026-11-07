using Cysharp.Threading.Tasks;
using MagicOnion;

namespace VolTech.Client
{
    /// <summary>
    /// RPC の呼び出しの結果を ApiResult にする。アプリは try/catch を書かず、これを付けて呼ぶ。
    /// </summary>
    public static class ApiResultExtensions
    {
        /// <summary>
        /// 呼び出しの完了を待ち、結果を ApiResult にする。失敗は共通エラーハンドラーの処理の後に届く。
        /// </summary>
        /// <remarks>
        /// 受け止めるのは ApiException だけ。それ以外の例外はそのまま投げて、不具合を隠さない。
        /// </remarks>
        /// <param name="call">RPC の呼び出し。</param>
        /// <typeparam name="T">レスポンスの型。</typeparam>
        /// <returns>呼び出しの結果。</returns>
        public static async UniTask<ApiResult<T>> ToResult<T>(this UnaryResult<T> call)
        {
            try
            {
                return ApiResult<T>.Success(await call);
            }
            catch (ApiException exception)
            {
                return ApiResult<T>.Failure(exception.Error);
            }
        }
    }
}
