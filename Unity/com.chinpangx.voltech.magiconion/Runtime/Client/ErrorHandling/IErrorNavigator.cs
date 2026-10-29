using Cysharp.Threading.Tasks;

namespace VolTech.Client
{
    /// <summary>
    /// エラーの後の画面遷移。アプリが実装し、ApiCore に渡す。
    /// </summary>
    /// <remarks>
    /// ApiCore は、セッションを終わらせ、順番待ちを解放してから呼ぶ。遷移先の画面は、遷移の途中から API を呼んでよい。
    /// </remarks>
    public interface IErrorNavigator
    {
        /// <summary>
        /// タイトル画面に戻る。
        /// </summary>
        public UniTask GoToTitleAsync();

        /// <summary>
        /// ホーム画面に戻る。
        /// </summary>
        public UniTask GoToHomeAsync();

        /// <summary>
        /// ストアのページを開く。
        /// </summary>
        public UniTask OpenStoreAsync();
    }
}
