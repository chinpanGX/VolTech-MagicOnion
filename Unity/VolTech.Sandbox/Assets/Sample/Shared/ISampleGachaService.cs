using MagicOnion;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// サンプルのガチャ。サンプルサーバーも同じファイルを読み込む。
    /// </summary>
    public interface ISampleGachaService : IService<ISampleGachaService>
    {
        /// <summary>ホームの全件を返す。</summary>
        [NoSideEffects]
        UnaryResult<HomeResponse> GetHomeAsync();

        /// <summary>ガチャを 1 回引く。</summary>
        UnaryResult<DrawResponse> DrawAsync(DrawRequest request);
    }
}
