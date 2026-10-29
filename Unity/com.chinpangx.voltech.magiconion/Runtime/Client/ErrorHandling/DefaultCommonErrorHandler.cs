using Cysharp.Threading.Tasks;

namespace VolTech.Client
{
    /// <summary>
    /// 共通エラーハンドラーの既定の実装。action ごとに決まったボタンのダイアログを出し、押されたボタンから動きを決める。
    /// </summary>
    /// <remarks>
    /// 画面の遷移はしない。決めた動きを返すと、ApiCore がセッションを終わらせてから遷移する。
    /// </remarks>
    public sealed class DefaultCommonErrorHandler : ICommonErrorHandler
    {
        private readonly IErrorDialogView dialog;

        /// <summary>
        /// ダイアログの見た目を受け取って作る。
        /// </summary>
        public DefaultCommonErrorHandler(IErrorDialogView dialog)
        {
            this.dialog = dialog;
        }

        /// <summary>
        /// action に合わせたダイアログを出し、押されたボタンに合わせた動きを返す。
        /// </summary>
        public async UniTask<ErrorDecision> HandleAsync(ApiError error)
        {
            switch (error.Action)
            {
                case ErrorAction.Retry:
                    var pressed = await dialog.ShowAsync(error, ErrorDialogButtons.RetryOrTitle);
                    return pressed == ErrorDialogButton.Retry ? ErrorDecision.Retry : ErrorDecision.ReturnToTitle;
                case ErrorAction.Stay:
                    await dialog.ShowAsync(error, ErrorDialogButtons.Ok);
                    return ErrorDecision.Stay;
                case ErrorAction.Home:
                    await dialog.ShowAsync(error, ErrorDialogButtons.Home);
                    return ErrorDecision.ReturnToHome;
                case ErrorAction.Update:
                    // アップデートするまで先へ進めないので、ストアを開いた後も画面には戻さない
                    await dialog.ShowAsync(error, ErrorDialogButtons.Store);
                    return ErrorDecision.OpenStore;
                default:
                    await dialog.ShowAsync(error, ErrorDialogButtons.Title);
                    return ErrorDecision.ReturnToTitle;
            }
        }
    }
}
