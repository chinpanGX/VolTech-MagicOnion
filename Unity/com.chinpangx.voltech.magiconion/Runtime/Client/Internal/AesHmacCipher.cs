using System;
using System.Buffers;
using System.Security.Cryptography;

namespace VolTech.Client
{
    /// <summary>
    /// AES で暗号化し、HMAC-SHA256 で改ざんと破損を検知する。端末に保存するアカウント情報に使う。
    /// </summary>
    /// <remarks>
    /// 自作パッケージ Supplement の AesHmacCryptoAlgorithm を、パッケージとしては依存せずにコピーして作り変えたもの。
    /// 形式は <c>[ Magic "VTA1"(4) | Version(1) | SaltLength(2, BE) | Salt | Cipher | Mac(32) ]</c>。
    /// AES 鍵・IV・HMAC 鍵は、パスワードと保存ごとにランダムなソルトから PBKDF2 で導く。
    /// 壊れた・書き換えられたデータは、ヘッダの不正も含めてすべて CryptographicException で知らせる。
    /// </remarks>
    internal static class AesHmacCipher
    {
        private const uint Magic = 0x56544131; // 'V' 'T' 'A' '1'
        private const byte Version = 0x01;
        private const int HeaderSize = 4 + 1 + 2; // Magic(4) + Version(1) + SaltLength(2)
        private const int MaxSaltSize = 1024;
        private const int MacSize = 32;

        // Supplement の既定の設定のまま(128bit 鍵、反復 1000 回、16 バイトのソルト、CBC、PKCS7、SHA-256)。
        // 鍵の元がアプリの中にあるので暗号化は難読化の位置づけで、PBKDF2 は本来は要らないが、動作を確かめたコードを変えないために残す
        private const int KeySize = 16;
        private const int IterationCount = 1000;
        private const int SaltSize = 16;

        /// <summary>
        /// 暗号化し、ヘッダ・ソルト・暗号文・Mac を並べたバイト列を返す。
        /// </summary>
        internal static byte[] Encrypt(byte[] plainBytes, string password)
        {
            var salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            using var aes = CreateAes();
            var macKey = SetKeyAndIv(aes, password, salt);

            using var encryptor = aes.CreateEncryptor();
            var cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            var result = new byte[HeaderSize + salt.Length + cipher.Length + MacSize];
            var offset = WriteHeader(result, salt);
            Buffer.BlockCopy(cipher, 0, result, offset, cipher.Length);
            offset += cipher.Length;

            // ヘッダ・ソルト・暗号文の全体にかけ、復号の前に改ざんと破損を検知できるようにする
            using var hmac = new HMACSHA256(macKey);
            hmac.TryComputeHash(new ReadOnlySpan<byte>(result, 0, offset), new Span<byte>(result, offset, MacSize), out _);
            return result;
        }

        /// <summary>
        /// Mac を照合してから復号する。
        /// </summary>
        /// <exception cref="CryptographicException">データが壊れている・書き換えられている、またはパスワードが違う。</exception>
        internal static byte[] Decrypt(byte[] cipherBytes, string password)
        {
            if (cipherBytes.Length <= HeaderSize)
            {
                throw new CryptographicException("Cipher bytes are too short to contain header and salt.");
            }

            var offset = ReadHeader(cipherBytes, out var salt);

            using var aes = CreateAes();
            var macKey = SetKeyAndIv(aes, password, salt);

            // 復号より先に Mac を照合する。CBC は暗号文を書き換えると平文も狙い通りに変わり、
            // パディングさえ通れば復号に成功してしまうため、Mac で弾かないと改ざんや破損に気付けない
            var macOffset = cipherBytes.Length - MacSize;
            var computedMac = ArrayPool<byte>.Shared.Rent(MacSize);
            try
            {
                using var hmac = new HMACSHA256(macKey);
                hmac.TryComputeHash(new ReadOnlySpan<byte>(cipherBytes, 0, macOffset), computedMac, out _);

                if (!CryptographicOperations.FixedTimeEquals(
                        new ReadOnlySpan<byte>(computedMac, 0, MacSize),
                        new ReadOnlySpan<byte>(cipherBytes, macOffset, MacSize)))
                {
                    throw new CryptographicException(
                        "Cipher authentication failed. The data is corrupted, tampered, or the password is wrong.");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(computedMac);
            }

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipherBytes, offset, macOffset - offset);
        }

        private static Aes CreateAes()
        {
            var aes = Aes.Create();
            // AES のブロックサイズは鍵長に関わらず 128bit 固定なので、BlockSize は既定のままにする
            aes.KeySize = KeySize * 8;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        // パスワードとソルトから PBKDF2 で AES 鍵と IV を導いて aes に設定し、続けて導いた HMAC 鍵を返す
        private static byte[] SetKeyAndIv(Aes aes, string password, byte[] salt)
        {
            using var deriveBytes = new Rfc2898DeriveBytes(password, salt, IterationCount, HashAlgorithmName.SHA256);
            aes.Key = deriveBytes.GetBytes(aes.KeySize / 8);
            aes.IV = deriveBytes.GetBytes(aes.BlockSize / 8);
            return deriveBytes.GetBytes(MacSize);
        }

        // ヘッダとソルトを書き込み、続きを書く位置を返す
        private static int WriteHeader(byte[] buffer, byte[] salt)
        {
            buffer[0] = (byte)(Magic >> 24 & 0xFF);
            buffer[1] = (byte)(Magic >> 16 & 0xFF);
            buffer[2] = (byte)(Magic >> 8 & 0xFF);
            buffer[3] = (byte)(Magic & 0xFF);
            buffer[4] = Version;
            buffer[5] = (byte)(salt.Length >> 8);
            buffer[6] = (byte)salt.Length;
            Buffer.BlockCopy(salt, 0, buffer, HeaderSize, salt.Length);
            return HeaderSize + salt.Length;
        }

        // ヘッダを確かめてソルトを取り出し、暗号文の始まる位置を返す
        private static int ReadHeader(byte[] bytes, out byte[] salt)
        {
            var magic = (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
            if (magic != Magic)
            {
                throw new CryptographicException("Invalid cipher format: magic mismatch.");
            }

            if (bytes[4] != Version)
            {
                throw new CryptographicException($"Unsupported cipher version: {bytes[4]}.");
            }

            var saltLength = bytes[5] << 8 | bytes[6];
            if (saltLength == 0 || saltLength > MaxSaltSize)
            {
                throw new CryptographicException($"Invalid salt length: {saltLength}.");
            }

            if (bytes.Length < HeaderSize + saltLength + 1 + MacSize)
            {
                // ソルトと最低 1 バイトの暗号文と Mac が入っていない
                throw new CryptographicException("Cipher bytes are too short for the specified salt length.");
            }

            salt = new byte[saltLength];
            Buffer.BlockCopy(bytes, HeaderSize, salt, 0, saltLength);
            return HeaderSize + saltLength;
        }
    }
}
