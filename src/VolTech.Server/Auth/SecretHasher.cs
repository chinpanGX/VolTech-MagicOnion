using System.Globalization;
using System.Security.Cryptography;

namespace VolTech.Server.Auth
{
    /// <summary>
    /// デバイスの secretKey をハッシュにして保存し、照合する。
    /// </summary>
    /// <remarks>
    /// secretKey はクライアントが作る乱数で、人が覚えるパスワードではない。総当たりへの備えは鍵の長さで足りるので、
    /// Rust 版の Argon2 ではなく、.NET に入っている PBKDF2 を使い、依存を増やさない。
    /// 形は "pbkdf2-sha256$回数$salt$hash"(Base64)。回数を変えても、保存済みのハッシュは照合できる。
    /// </remarks>
    internal static class SecretHasher
    {
        private const string Scheme = "pbkdf2-sha256";
        private const int Iterations = 10_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static string Hash(string secret)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return string.Join(
                '$',
                Scheme,
                Iterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }

        public static bool Verify(string secret, string stored)
        {
            var parts = stored.Split('$');
            if (parts.Length != 4
                || parts[0] != Scheme
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
