using Cysharp.Threading.Tasks;

namespace VolTech.Client.Auth
{
    /// <summary>
    /// メモリに持つだけの IAccountStore。テスト用で、アプリでは使わない(起動するたびに別の端末として登録され、前のプレイヤーに戻れなくなる)。
    /// アプリでは EncryptedFileAccountStore(Unity では UnityAccountStore.Create)を使う。
    /// </summary>
    public sealed class InMemoryAccountStore : IAccountStore
    {
        private AccountData data;

        /// <summary>
        /// 持っているアカウント情報を返す。
        /// </summary>
        public UniTask<AccountData> LoadAsync()
        {
            return UniTask.FromResult(data);
        }

        /// <summary>
        /// アカウント情報をメモリに持つ。
        /// </summary>
        public UniTask SaveAsync(AccountData data)
        {
            this.data = data;
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 持っているアカウント情報を捨てる。
        /// </summary>
        public UniTask ClearAsync()
        {
            data = null;
            return UniTask.CompletedTask;
        }
    }
}
