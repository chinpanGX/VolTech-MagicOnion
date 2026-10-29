using System;

namespace VolTech.Client.Auth
{
    /// <summary>
    /// デバイス登録はできたが、アカウント情報を端末に保存できなかったことを表す(空き容量が無い、書き込めない、など)。
    /// </summary>
    /// <remarks>
    /// 登録した結果は DeviceTokenProvider がメモリに持ち、次に呼ばれたときに登録し直さずに保存からやり直す。
    /// 保存できないまま進めると、次に起動したときに別の端末として登録されて、そのプレイヤーに戻れなくなるので、トークンは発行しない。
    /// </remarks>
    public sealed class AccountSaveFailedException : Exception
    {
        /// <summary>
        /// 保存できなかった理由を受け取って作る。
        /// </summary>
        /// <param name="innerException">IAccountStore.SaveAsync が投げた例外。</param>
        public AccountSaveFailedException(Exception innerException)
            : base("Failed to save the registered account.", innerException)
        {
        }
    }
}
