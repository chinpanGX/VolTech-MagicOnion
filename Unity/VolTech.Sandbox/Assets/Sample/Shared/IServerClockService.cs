using MagicOnion;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// サーバーの時刻を変える開発用のサービス。Server Clock ウィンドウが呼ぶ。
    /// </summary>
    /// <remarks>
    /// 認証なしで時刻を変えられるので、本番のサーバーに写さない。サンプルサーバーは localhost だけで待ち受ける。
    /// </remarks>
    [SkipAuth]
    public interface IServerClockService : IService<IServerClockService>
    {
        /// <summary>今の時刻を返す。</summary>
        UnaryResult<ServerClockInfo> GetAsync();

        /// <summary>本当の時刻からずらす秒数を絶対値で決める。0 で元に戻る。</summary>
        UnaryResult<ServerClockInfo> SetOffsetAsync(long seconds);
    }
}
