using System.Buffers;
using Grpc.Core;
using MagicOnion.Server;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using VolTech.Server.Abstractions;
using VolTech.Server.Internal;
using VolTech.Server.Logging;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// <see cref="ITransactions"/> の実装。
    /// </summary>
    internal sealed class Transactions : ITransactions
    {
        private readonly NpgsqlDataSource dataSource;
        private readonly ILogger<Transactions> logger;

        public Transactions(NpgsqlDataSource dataSource, ILogger<Transactions> logger)
        {
            this.dataSource = dataSource;
            this.logger = logger;
        }

        public async Task<TResponse> RunAsync<TResponse>(ServiceContext context, Func<TxContext, Task<TResponse>> handler)
        {
            var settings = RpcSettings.Get(context);
            var playerId = context.FindAuthContext()?.PlayerId;
            var cancellationToken = context.CallContext.CancellationToken;
            var requestId = ReadRequestId(context);

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (settings.NoSideEffects)
            {
                // 誤って書き込むと DB がその場でエラーを返すので、宣言の間違いに開発中に気づける
                await ExecuteAsync(connection, transaction, "SET TRANSACTION READ ONLY", cancellationToken);
                var readOnlyContext = new TxContext(connection, transaction, playerId, isReadOnly: true, cancellationToken);
                var response = await handler(readOnlyContext);
                await transaction.CommitAsync(cancellationToken);
                return response;
            }

            var txContext = new TxContext(connection, transaction, playerId, isReadOnly: false, cancellationToken);
            // [SkipAuth] の RPC はプレイヤーが決まらないので、冪等性の対象にしない
            if (playerId == null)
            {
                var response = await handler(txContext);
                await CommitAsync(txContext, settings.FullName, requestId);
                return response;
            }

            if (requestId == null)
            {
                throw new RpcException(new Grpc.Core.Status(
                    StatusCode.InvalidArgument,
                    $"{ApiHeaders.RequestId} must be 1..{RequestIdRules.MaxLength} alphanumeric characters or '-'"));
            }

            return await RunTargetAsync(context, txContext, playerId.Value, settings.FullName, requestId, handler);
        }

        private async Task<TResponse> RunTargetAsync<TResponse>(
            ServiceContext context,
            TxContext txContext,
            Ulid playerId,
            string rpc,
            string requestId,
            Func<TxContext, Task<TResponse>> handler)
        {
            var cancellationToken = txContext.CancellationToken;
            // 再送が届いても最初の処理の完了を待てるように、業務データより先に直前のレスポンスの行をロックする。
            // 行が無ければ作る(PostgreSQL は MySQL と違い隙間のロックを取らないので、同じトランザクションで作ってよい)
            await using (var insert = txContext.CreateCommand(
                "INSERT INTO last_responses (player_id) VALUES ($1) ON CONFLICT (player_id) DO NOTHING"))
            {
                insert.Parameters.AddWithValue(playerId.ToString());
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var select = txContext.CreateCommand(
                "SELECT request_id, rpc, response FROM last_responses WHERE player_id = $1 FOR UPDATE"))
            {
                select.Parameters.AddWithValue(playerId.ToString());
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                var savedRequestId = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (savedRequestId == requestId)
                {
                    var savedRpc = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (savedRpc != rpc)
                    {
                        // クライアントの不具合で RequestID が使い回されたとき、型の違うレスポンスを返さない
                        logger.LogWarning("x-request-id was already used for another RPC {saved_rpc}", savedRpc);
                        throw new RpcException(new Grpc.Core.Status(
                            StatusCode.InvalidArgument,
                            $"{ApiHeaders.RequestId} was already used for {savedRpc}"));
                    }

                    var saved = reader.GetFieldValue<byte[]>(2);
                    SetReplayed(context, true);
                    return context.MessageSerializer.Deserialize<TResponse>(new ReadOnlySequence<byte>(saved));
                }
            }

            SetReplayed(context, false);
            var response = await handler(txContext);

            var buffer = new ArrayBufferWriter<byte>();
            context.MessageSerializer.Serialize(buffer, response);
            await using (var update = txContext.CreateCommand(
                "UPDATE last_responses SET request_id = $1, rpc = $2, response = $3, updated_at = now() WHERE player_id = $4"))
            {
                update.Parameters.AddWithValue(requestId);
                update.Parameters.AddWithValue(rpc);
                update.Parameters.AddWithValue(buffer.WrittenSpan.ToArray());
                update.Parameters.AddWithValue(playerId.ToString());
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await CommitAsync(txContext, rpc, requestId);
            return response;
        }

        /// <summary>ゲームログを同じトランザクションで書いてから、コミットする。</summary>
        private static async Task CommitAsync(TxContext txContext, string rpc, string? requestId)
        {
            var cancellationToken = txContext.CancellationToken;
            foreach (var log in txContext.GameLogs)
            {
                await using var command = txContext.CreateCommand(
                    "INSERT INTO game_logs (player_id, request_id, rpc, kind, body) VALUES ($1, $2, $3, $4, $5)");
                command.Parameters.AddWithValue(txContext.PlayerId!.Value.ToString());
                command.Parameters.AddWithValue((object?)requestId ?? DBNull.Value);
                command.Parameters.AddWithValue(rpc);
                command.Parameters.AddWithValue(log.Kind);
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = log.ToBody().ToJsonString() });
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await txContext.Transaction.CommitAsync(cancellationToken);
        }

        private static async Task ExecuteAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string sql,
            CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static string? ReadRequestId(ServiceContext context)
        {
            var value = context.CallContext.RequestHeaders.GetValue(ApiHeaders.RequestId);
            return RequestIdRules.IsValid(value) ? value : null;
        }

        private static void SetReplayed(ServiceContext context, bool replayed)
        {
            var scope = RpcLogScope.Find(context);
            if (scope != null)
            {
                scope.Replayed = replayed;
            }
        }
    }
}
