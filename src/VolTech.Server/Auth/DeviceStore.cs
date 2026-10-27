using Npgsql;

namespace VolTech.Server.Auth
{
    /// <summary>
    /// devices・access_tokens テーブルの読み書き。
    /// </summary>
    internal static class DeviceStore
    {
        public static async Task InsertDeviceAsync(
            NpgsqlDataSource dataSource,
            string deviceId,
            Ulid playerId,
            string secretHash,
            CancellationToken cancellationToken)
        {
            await using var command = dataSource.CreateCommand(
                "INSERT INTO devices (device_id, player_id, secret_hash) VALUES ($1, $2, $3)");
            command.Parameters.AddWithValue(deviceId);
            command.Parameters.AddWithValue(playerId.ToGuid());
            command.Parameters.AddWithValue(secretHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<DeviceRow?> FindDeviceAsync(
            NpgsqlDataSource dataSource,
            string deviceId,
            CancellationToken cancellationToken)
        {
            await using var command = dataSource.CreateCommand(
                "SELECT player_id, secret_hash FROM devices WHERE device_id = $1");
            command.Parameters.AddWithValue(deviceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new DeviceRow(new Ulid(reader.GetGuid(0)), reader.GetString(1));
        }

        public static async Task UpsertTokenAsync(
            NpgsqlDataSource dataSource,
            string deviceId,
            string token,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken)
        {
            await using var command = dataSource.CreateCommand(
                "INSERT INTO access_tokens (device_id, token, expires_at) VALUES ($1, $2, $3) "
                + "ON CONFLICT (device_id) DO UPDATE SET token = excluded.token, expires_at = excluded.expires_at");
            command.Parameters.AddWithValue(deviceId);
            command.Parameters.AddWithValue(token);
            command.Parameters.AddWithValue(expiresAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<TokenRow?> FindTokenAsync(
            NpgsqlDataSource dataSource,
            string token,
            CancellationToken cancellationToken)
        {
            // 期限切れと、知らないトークンをログで見分けるため、期限は WHERE ではなく値として読む
            await using var command = dataSource.CreateCommand(
                "SELECT d.device_id, d.player_id, t.expires_at FROM access_tokens t "
                + "JOIN devices d ON d.device_id = t.device_id WHERE t.token = $1");
            command.Parameters.AddWithValue(token);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new TokenRow(reader.GetString(0), new Ulid(reader.GetGuid(1)), reader.GetFieldValue<DateTimeOffset>(2));
        }
    }

    internal sealed record DeviceRow(Ulid PlayerId, string SecretHash);

    internal sealed record TokenRow(string DeviceId, Ulid PlayerId, DateTimeOffset ExpiresAt);
}
