using MagicOnion.Server;
using VolTech.Server.Auth;
using VolTech.Server.Internal;

namespace VolTech.Server
{
    /// <summary>
    /// 基盤のフィルターが ServiceContext に入れた値を取り出す。
    /// </summary>
    public static class ServiceContextExtensions
    {
        /// <summary>
        /// リクエストを受けたときのゲーム内の日付。レスポンスヘッダー x-game-date と同じ日付。
        /// 日付で決まる処理(1 日 1 回の報酬など)にはこれを使う。
        /// </summary>
        /// <exception cref="InvalidOperationException">基盤のフィルターを通っていないとき(AddVoltechServer を呼んでいない)。</exception>
        public static DateOnly GetGameDate(this ServiceContext context)
        {
            if (context.Items.TryGetValue(ContextItems.GameDate, out var value) && value is DateOnly date)
            {
                return date;
            }

            throw new InvalidOperationException("ゲーム内の日付が無い。AddVoltechServer() で基盤のフィルターを登録しているか確かめる。");
        }

        /// <summary>
        /// 認証したデバイスとプレイヤー。
        /// </summary>
        /// <exception cref="InvalidOperationException">認証していない RPC([SkipAuth])で呼んだとき。</exception>
        public static AuthContext GetAuthContext(this ServiceContext context)
        {
            return context.FindAuthContext()
                ?? throw new InvalidOperationException("AuthContext が無い。[SkipAuth] の RPC では使えない。");
        }

        /// <summary>
        /// 認証したデバイスとプレイヤー。認証していない RPC([SkipAuth])では null。
        /// </summary>
        public static AuthContext? FindAuthContext(this ServiceContext context)
        {
            return context.Items.TryGetValue(ContextItems.AuthContext, out var value) ? value as AuthContext : null;
        }
    }
}
