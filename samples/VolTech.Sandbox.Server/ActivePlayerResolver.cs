using VolTech.Server.Auth;

namespace VolTech.Sandbox.Server
{
    /// <summary>
    /// プレイヤーの作成が無いサンプルなので、いつも作られている扱いにする。
    /// </summary>
    public sealed class ActivePlayerResolver : IPlayerResolver
    {
        public ValueTask<PlayerStatus> ResolveAsync(Ulid playerId, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(PlayerStatus.Active);
        }
    }
}
