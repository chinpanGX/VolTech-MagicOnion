using Cysharp.Threading.Tasks;

namespace VolTech.Client
{
    /// <summary>
    /// エラーダイアログの見た目。アプリが実装する。
    /// </summary>
    public interface IErrorDialogView
    {
        /// <summary>
        /// ダイアログを出し、押されたボタンを返す。
        /// </summary>
        public UniTask<ErrorDialogButton> ShowAsync(ApiError error, ErrorDialogButtons buttons);
    }
}
