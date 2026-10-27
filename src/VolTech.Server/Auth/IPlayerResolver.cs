namespace VolTech.Server.Auth
{
    /// <summary>
    /// playerId からプレイヤーの状態を引く。players テーブルを持つアプリが実装し、DI に登録する。
    /// </summary>
    /// <remarks>
    /// リクエストごとのスコープから取るので、scoped で登録してよい。
    /// </remarks>
    public interface IPlayerResolver
    {
        ValueTask<PlayerStatus> ResolveAsync(Ulid playerId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// プレイヤーの状態。アカウント停止(Suspended)は持たない。
    /// </summary>
    public enum PlayerStatus
    {
        /// <summary>playerId は発行済みだが、プレイヤーがまだ作られていない(サインアップの前)。</summary>
        NotCreated,

        /// <summary>プレイヤーが作られている。</summary>
        Active,
    }
}
