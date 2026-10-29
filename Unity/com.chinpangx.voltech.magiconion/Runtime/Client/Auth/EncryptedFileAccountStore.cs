using System;
using System.IO;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using Google.Protobuf;
using VolTech.V1;

namespace VolTech.Client.Auth
{
    /// <summary>
    /// アカウント情報を、暗号化したファイルに保存する IAccountStore。基盤の既定の保存先。
    /// </summary>
    /// <remarks>
    /// ファイルは StoredAccount(VolTech-gRPC の voltech/v1/account_store.proto から生成した Generated/AccountStore.cs)の Protobuf のバイナリを、AES で暗号化して HMAC を付けたもの。
    /// 項目を後から足しても古いファイルを読める。書き込みは一時ファイルに書いてから置き換えるので、途中で落ちても壊れない。
    /// 鍵の元がアプリの中にあるので、暗号化は難読化の位置づけ(他のアプリからは OS のサンドボックスで守られる)。
    /// 本当に守りたいアプリは、iOS の Keychain や Android の Keystore を使う IAccountStore に差し替える。
    /// 保存先のパスは Application.persistentDataPath の下に、鍵の元はバンドルID(Application.identifier)にする。
    /// </remarks>
    public sealed class EncryptedFileAccountStore : IAccountStore
    {
        // 鍵の元に足す、パッケージで決めた値。鍵の元(バンドルID)だけでパスワードにならないようにする
        private const string PasswordPrefix = "VolTech.Client.Auth.EncryptedFileAccountStore:";

        private readonly string filePath;
        private readonly string password;

        /// <summary>
        /// 保存するファイルと鍵の元を受け取って作る。
        /// </summary>
        /// <param name="filePath">保存するファイルのパス。接続先(サーバーの URL)ごとに分ける。</param>
        /// <param name="keySource">鍵の元(Unity ではバンドルID)。ファイルには含めない。変えると、それまでのファイルは読めなくなる。</param>
        /// <exception cref="ArgumentException">filePath か keySource が null か空のとき。</exception>
        public EncryptedFileAccountStore(string filePath, string keySource)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));
            }

            if (string.IsNullOrEmpty(keySource))
            {
                throw new ArgumentException("Key source must not be null or empty.", nameof(keySource));
            }

            this.filePath = filePath;
            password = PasswordPrefix + keySource;
        }

        /// <summary>
        /// 保存したアカウント情報を読む。ファイルが無ければ(まだ登録していなければ)null を返す。
        /// </summary>
        /// <exception cref="CorruptedAccountDataException">ファイルはあるのに読めないとき(壊れている・書き換えられている・値が空)。</exception>
        public async UniTask<AccountData> LoadAsync()
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            // Task は AsUniTask() を通さずにそのまま await する(本体の決まり)
            var bytes = await File.ReadAllBytesAsync(filePath);

            StoredAccount stored;
            try
            {
                stored = StoredAccount.Parser.ParseFrom(AesHmacCipher.Decrypt(bytes, password));
            }
            catch (CryptographicException exception)
            {
                throw new CorruptedAccountDataException(filePath, exception);
            }
            catch (InvalidProtocolBufferException exception)
            {
                throw new CorruptedAccountDataException(filePath, exception);
            }

            // 3つともデバイス登録で揃うので、どれかが空なら壊れている
            if (stored.DeviceId.Length == 0 || stored.SecretKey.Length == 0 || stored.PlayerId.Length == 0)
            {
                throw new CorruptedAccountDataException(filePath, null);
            }

            return new AccountData(stored.DeviceId, stored.SecretKey, stored.PlayerId);
        }

        /// <summary>
        /// アカウント情報を暗号化して保存する(上書き)。
        /// </summary>
        public UniTask SaveAsync(AccountData data)
        {
            var stored = new StoredAccount
            {
                DeviceId = data.DeviceId,
                SecretKey = data.SecretKey,
                PlayerId = data.PlayerId,
            };
            return SafeFile.WriteAllBytesAsync(filePath, AesHmacCipher.Encrypt(stored.ToByteArray(), password));
        }

        /// <summary>
        /// 保存したアカウント情報のファイルを消す。無ければ何もしない。
        /// </summary>
        public UniTask ClearAsync()
        {
            return SafeFile.DeleteAsync(filePath);
        }
    }
}
