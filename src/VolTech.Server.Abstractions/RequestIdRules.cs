namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// x-request-id の形の決まり。1〜64 文字で、英数字と "-" だけ。
    /// </summary>
    public static class RequestIdRules
    {
        /// <summary>最大の長さ。</summary>
        public const int MaxLength = 64;

        /// <summary>
        /// RequestID として使える値か。
        /// </summary>
        public static bool IsValid(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            {
                return false;
            }

            foreach (var character in value)
            {
                var isDigit = character >= '0' && character <= '9';
                var isLower = character >= 'a' && character <= 'z';
                var isUpper = character >= 'A' && character <= 'Z';
                if (!isDigit && !isLower && !isUpper && character != '-')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
