using System;

namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// エラーコード。google.rpc.ErrorInfo の domain と reason の組で見分ける。
    /// </summary>
    /// <remarks>
    /// reason が同じでも domain が違えば別のエラー。基盤の予約エラーは domain "voltech"、アプリの業務エラーはアプリの domain を使う。
    /// 定数は C# で直接書く。予約エラーはこのプロジェクトに、業務エラーはアプリの共有コードに置く。
    /// </remarks>
    public readonly struct ErrorCode : IEquatable<ErrorCode>
    {
        /// <summary>
        /// domain と reason からエラーコードを作る。
        /// </summary>
        /// <exception cref="ArgumentNullException">domain か reason が null のとき。</exception>
        public ErrorCode(string domain, string reason)
        {
            Domain = domain ?? throw new ArgumentNullException(nameof(domain));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        /// <summary>ErrorInfo.domain に入れる値。</summary>
        public string Domain { get; }

        /// <summary>ErrorInfo.reason に入れる値。</summary>
        public string Reason { get; }

        public static bool operator ==(ErrorCode left, ErrorCode right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ErrorCode left, ErrorCode right)
        {
            return !left.Equals(right);
        }

        public bool Equals(ErrorCode other)
        {
            return string.Equals(Domain, other.Domain, StringComparison.Ordinal)
                && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is ErrorCode other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Domain, Reason);
        }

        /// <summary>
        /// "domain/reason" の形で返す。ログ用。
        /// </summary>
        public override string ToString()
        {
            return Domain + "/" + Reason;
        }
    }
}
