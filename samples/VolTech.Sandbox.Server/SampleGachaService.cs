using MagicOnion;
using MagicOnion.Server;
using VolTech.Sandbox.Sample;
using VolTech.Server;
using VolTech.Server.Abstractions;
using VolTech.Server.Transaction;

namespace VolTech.Sandbox.Server
{
    /// <summary>
    /// ガチャの値と表。
    /// </summary>
    public static class SampleGacha
    {
        public const int InitialGems = 3000;

        public const int Price = 300;

        public const string CreateTableSql =
            "CREATE TABLE IF NOT EXISTS sample_players ("
            + "player_id TEXT PRIMARY KEY, gems INTEGER NOT NULL, last_free_draw DATE)";

        public static readonly string[] Characters = { "アカリ", "ヒナタ", "ソラ" };
    }

    public sealed class SampleGachaService : ServiceBase<ISampleGachaService>, ISampleGachaService
    {
        private readonly ITransactions transactions;

        public SampleGachaService(ITransactions transactions)
        {
            this.transactions = transactions;
        }

        public async UnaryResult<HomeResponse> GetHomeAsync()
        {
            var today = Context.GetGameDate();
            var player = Context.GetAuthContext().PlayerId.ToString();
            return await transactions.RunAsync(Context, async tx =>
            {
                await using var command = tx.CreateCommand(
                    "SELECT gems, last_free_draw IS DISTINCT FROM $2 FROM sample_players WHERE player_id = $1");
                command.Parameters.AddWithValue(player);
                command.Parameters.AddWithValue(today);
                await using var reader = await command.ExecuteReaderAsync(Context.CallContext.CancellationToken);

                // 読み取り専用なので行は作らない。行が無ければ最初の状態として返す
                if (!await reader.ReadAsync(Context.CallContext.CancellationToken))
                {
                    return new HomeResponse { Gems = SampleGacha.InitialGems, FreeDrawAvailable = true };
                }

                return new HomeResponse { Gems = reader.GetInt32(0), FreeDrawAvailable = reader.GetBoolean(1) };
            });
        }

        public async UnaryResult<DrawResponse> DrawAsync(DrawRequest request)
        {
            var today = Context.GetGameDate();
            var player = Context.GetAuthContext().PlayerId.ToString();
            return await transactions.RunAsync(Context, async tx =>
            {
                await CreatePlayerAsync(tx, player);
                var freeDrawAvailable = request.UseFree
                    ? await DrawFreeAsync(tx, player, today)
                    : await DrawPaidAsync(tx, player, today);
                return new DrawResponse
                {
                    CharacterName = SampleGacha.Characters[Random.Shared.Next(SampleGacha.Characters.Length)],
                    FreeDrawAvailable = freeDrawAvailable,
                };
            });
        }

        private async Task CreatePlayerAsync(TxContext tx, string player)
        {
            await using var command = tx.CreateCommand(
                "INSERT INTO sample_players (player_id, gems) VALUES ($1, $2) ON CONFLICT DO NOTHING");
            command.Parameters.AddWithValue(player);
            command.Parameters.AddWithValue(SampleGacha.InitialGems);
            await command.ExecuteNonQueryAsync(Context.CallContext.CancellationToken);
        }

        private async Task<bool> DrawFreeAsync(TxContext tx, string player, DateOnly today)
        {
            await using var command = tx.CreateCommand(
                "UPDATE sample_players SET last_free_draw = $2 WHERE player_id = $1 AND last_free_draw IS DISTINCT FROM $2");
            command.Parameters.AddWithValue(player);
            command.Parameters.AddWithValue(today);
            if (await command.ExecuteNonQueryAsync(Context.CallContext.CancellationToken) == 0)
            {
                throw new BusinessException(SampleErrors.FreeDrawUsed, "free draw already used");
            }

            tx.WriteGameLog(GameLog.Event("gacha", "free_draw"));
            return false;
        }

        private async Task<bool> DrawPaidAsync(TxContext tx, string player, DateOnly today)
        {
            await using var command = tx.CreateCommand(
                "UPDATE sample_players SET gems = gems - $2 WHERE player_id = $1 AND gems >= $2 "
                + "RETURNING gems, last_free_draw IS DISTINCT FROM $3");
            command.Parameters.AddWithValue(player);
            command.Parameters.AddWithValue(SampleGacha.Price);
            command.Parameters.AddWithValue(today);
            await using var reader = await command.ExecuteReaderAsync(Context.CallContext.CancellationToken);
            if (!await reader.ReadAsync(Context.CallContext.CancellationToken))
            {
                throw new BusinessException(SampleErrors.NotEnoughGems, "not enough gems");
            }

            var gems = reader.GetInt32(0);
            var freeDrawAvailable = reader.GetBoolean(1);
            tx.WriteGameLog(GameLog.Quantity("gems", -SampleGacha.Price, gems, "gacha_draw"));
            tx.GetCollector<SampleCollector>().Gems = gems;
            return freeDrawAvailable;
        }
    }
}
