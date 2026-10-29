using Cysharp.Threading.Tasks;

namespace VolTech.Client.Auth
{
    /// <summary>
    /// アカウント情報(device_id・secret_key・player_id)を端末に保存する。
    /// </summary>
    public interface IAccountStore
    {
        /// <summary>
        /// 保存したアカウント情報を読む。まだ登録していなければ null を返す。
        /// </summary>
        public UniTask<AccountData> LoadAsync();

        /// <summary>
        /// アカウント情報を保存する(上書き)。
        /// </summary>
        public UniTask SaveAsync(AccountData data);

        /// <summary>
        /// 保存したアカウント情報を消す。
        /// </summary>
        public UniTask ClearAsync();
    }
}
