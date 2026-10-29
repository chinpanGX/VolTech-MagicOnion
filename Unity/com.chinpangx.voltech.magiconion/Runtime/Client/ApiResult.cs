using System;

namespace VolTech.Client
{
    /// <summary>
    /// API の呼び出しの結果。成功ならレスポンス、失敗ならエラーを持つ。呼び出し側に例外は投げない。
    /// </summary>
    public readonly struct ApiResult<T>
    {
        private readonly T value;

        private ApiResult(T value, ApiError error)
        {
            this.value = value;
            Error = error;
        }

        /// <summary>成功したか。</summary>
        public bool IsSuccess => Error == null;

        /// <summary>失敗したときのエラー。成功なら null。</summary>
        public ApiError Error { get; }

        /// <summary>
        /// 成功したときのレスポンス。失敗したときに読むと例外を投げる(成功の確かめ忘れに、開発中に気づけるように)。
        /// </summary>
        public T Value
        {
            get
            {
                if (!IsSuccess)
                {
                    throw new InvalidOperationException($"失敗した結果の Value を読んだ: {Error}");
                }

                return value;
            }
        }

        /// <summary>
        /// 成功の結果を作る。
        /// </summary>
        internal static ApiResult<T> Success(T value)
        {
            return new ApiResult<T>(value, null);
        }

        /// <summary>
        /// 失敗の結果を作る。
        /// </summary>
        internal static ApiResult<T> Failure(ApiError error)
        {
            return new ApiResult<T>(default, error ?? throw new ArgumentNullException(nameof(error)));
        }
    }
}
