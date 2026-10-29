using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Client.Auth;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// EncryptedFileAccountStore の、暗号化したファイルへの保存と、壊れたファイルの扱いを確かめる。
    /// </summary>
    [Description("EncryptedFileAccountStore の、暗号化したファイルへの保存と、壊れたファイルの扱いを確かめる")]
    public class EncryptedFileAccountStoreTests
    {
        private const string KeySource = "com.example.app";

        // static フィールドは持たない(Unity 7 は static フィールドに属性を求める。UAL0010)
        private readonly AccountData account = new("device-1", "secret-1", "player-1");

        private string directory;
        private string filePath;
        private EncryptedFileAccountStore store;

        /// <summary>
        /// テストごとに、空の一時フォルダの中のファイルに保存するストアを作る。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "VolTechMagicOnionTests", Guid.NewGuid().ToString("N"));
            filePath = Path.Combine(directory, "account.bin");
            store = new EncryptedFileAccountStore(filePath, KeySource);
        }

        /// <summary>
        /// 一時フォルダを消す。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// ファイルが無ければ、未登録として null を返す。
        /// </summary>
        [UnityTest]
        [Description("ファイルが無ければ、未登録として null を返す")]
        public IEnumerator LoadReturnsNullWithoutFile() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(await store.LoadAsync(), Is.Null);
        });

        /// <summary>
        /// 保存したアカウント情報は、同じパスと鍵の元で作った別のストアから読める。ファイルには値がそのままの形で入らない。
        /// </summary>
        [UnityTest]
        [Description("保存したアカウント情報は、同じパスと鍵の元で作った別のストアから読める。ファイルには値がそのままの形で入らない")]
        public IEnumerator SavedAccountIsReadBack() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveAsync(account);

            var loaded = await new EncryptedFileAccountStore(filePath, KeySource).LoadAsync();

            Assert.That(loaded.DeviceId, Is.EqualTo("device-1"));
            Assert.That(loaded.SecretKey, Is.EqualTo("secret-1"));
            Assert.That(loaded.PlayerId, Is.EqualTo("player-1"));
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(filePath));
            Assert.That(text, Does.Not.Contain("secret-1"));
        });

        /// <summary>
        /// 上書きして保存すると、後の値が読める。一時ファイルは残らない。
        /// </summary>
        [UnityTest]
        [Description("上書きして保存すると、後の値が読める。一時ファイルは残らない")]
        public IEnumerator SaveOverwritesWithoutLeavingTempFile() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveAsync(account);
            await store.SaveAsync(new AccountData("device-2", "secret-2", "player-2"));

            Assert.That((await store.LoadAsync()).DeviceId, Is.EqualTo("device-2"));
            Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName), Is.EqualTo(new[] { "account.bin" }));
        });

        /// <summary>
        /// ファイルの1バイトでも書き換わっていたら、Mac の照合で弾き、専用の例外にする(新しいアカウントは作らない)。
        /// </summary>
        [UnityTest]
        [Description("ファイルの1バイトでも書き換わっていたら、Mac の照合で弾き、専用の例外にする(新しいアカウントは作らない)")]
        public IEnumerator TamperedFileThrows() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveAsync(account);
            var bytes = File.ReadAllBytes(filePath);
            // ヘッダとソルトの後ろの暗号文を書き換える
            bytes[bytes.Length / 2] ^= 0x01;
            File.WriteAllBytes(filePath, bytes);

            var exception = await LoadExpectingCorruptionAsync(store);

            Assert.That(exception.InnerException, Is.InstanceOf<CryptographicException>());
            Assert.That(File.Exists(filePath), Is.True, "読めないファイルを消した");
        });

        /// <summary>
        /// 鍵の元が違うと読めず、専用の例外にする。
        /// </summary>
        [UnityTest]
        [Description("鍵の元が違うと読めず、専用の例外にする")]
        public IEnumerator DifferentKeySourceThrows() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveAsync(account);

            await LoadExpectingCorruptionAsync(new EncryptedFileAccountStore(filePath, "com.example.other"));
        });

        /// <summary>
        /// 途中までしか無いファイルや、形式の違うファイルも、専用の例外にする。
        /// </summary>
        [UnityTest]
        [Description("途中までしか無いファイルや、形式の違うファイルも、専用の例外にする")]
        public IEnumerator TruncatedOrForeignFileThrows() => UniTask.ToCoroutine(async () =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(filePath, new byte[] { 0x56, 0x54, 0x41 });
            await LoadExpectingCorruptionAsync(store);

            File.WriteAllBytes(filePath, Encoding.UTF8.GetBytes("{\"deviceId\":\"device-1\",\"secretKey\":\"secret-1\"}"));
            await LoadExpectingCorruptionAsync(store);
        });

        /// <summary>
        /// 読めても値が空なら(デバイス登録で揃うはずの値が無い)、専用の例外にする。
        /// </summary>
        [UnityTest]
        [Description("読めても値が空なら(デバイス登録で揃うはずの値が無い)、専用の例外にする")]
        public IEnumerator EmptyValueThrows() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveAsync(new AccountData("device-1", "secret-1", ""));

            var exception = await LoadExpectingCorruptionAsync(store);

            Assert.That(exception.InnerException, Is.Null);
        });

        /// <summary>
        /// 消すとファイルが無くなり、次は未登録として読める。ファイルが無いときに消しても失敗しない。
        /// </summary>
        [UnityTest]
        [Description("消すとファイルが無くなり、次は未登録として読める。ファイルが無いときに消しても失敗しない")]
        public IEnumerator ClearDeletesFile() => UniTask.ToCoroutine(async () =>
        {
            await store.SaveAsync(account);

            await store.ClearAsync();
            await store.ClearAsync();

            Assert.That(File.Exists(filePath), Is.False);
            Assert.That(await store.LoadAsync(), Is.Null);
        });

        private static async UniTask<CorruptedAccountDataException> LoadExpectingCorruptionAsync(EncryptedFileAccountStore target)
        {
            try
            {
                await target.LoadAsync();
            }
            catch (CorruptedAccountDataException exception)
            {
                return exception;
            }

            Assert.Fail("読めないファイルなのに例外にならなかった");
            return null;
        }
    }
}
