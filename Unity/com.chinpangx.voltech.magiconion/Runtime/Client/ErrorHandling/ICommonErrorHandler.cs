using Cysharp.Threading.Tasks;

namespace VolTech.Client
{
    /// <summary>
    /// handledCodes で受け取らなかったエラーを処理する。ダイアログを出し、その後の動きを返す。
    /// </summary>
    public interface ICommonErrorHandler
    {
        /// <summary>
        /// エラーを処理し、送り直すか諦めるかを返す。ApiCore は1つずつ呼ぶ(ダイアログは同時に1つだけ)。
        /// </summary>
        /// <remarks>
        /// 画面の遷移はここでしない。タイトル・ホームに戻るなら ReturnToTitle・ReturnToHome を返すと、
        /// ApiCore がセッションを終わらせてから IErrorNavigator で遷移する。
        /// </remarks>
        public UniTask<ErrorDecision> HandleAsync(ApiError error);
    }
}
