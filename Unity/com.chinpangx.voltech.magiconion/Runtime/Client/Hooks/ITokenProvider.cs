using Cysharp.Threading.Tasks;

namespace VolTech.Client
{
    /// <summary>
    /// アクセストークンを渡す。デバイス登録・トークンの発行・期限の確認は実装の中で行う。
    /// </summary>
    public interface ITokenProvider
    {
        /// <summary>
        /// 有効なアクセストークンを返す。無ければ、デバイス登録やトークンの発行をしてから返す。
        /// </summary>
        public UniTask<string> GetTokenAsync();

        /// <summary>
        /// サーバーに UNAUTHENTICATED で弾かれたトークンを捨て、次の GetTokenAsync で発行し直させる。
        /// </summary>
        public void Invalidate(string rejectedToken);

        /// <summary>
        /// 覚えているトークンとアカウント情報を捨てる。次の GetTokenAsync で読み直す。
        /// </summary>
        public void Reset();
    }
}
