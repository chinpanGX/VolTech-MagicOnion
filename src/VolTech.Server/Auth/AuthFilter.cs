using Grpc.Core;
using MagicOnion;
using MagicOnion.Server;
using MagicOnion.Server.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using VolTech.Server.Abstractions;
using VolTech.Server.Internal;
using VolTech.Server.Logging;

namespace VolTech.Server.Auth
{
    /// <summary>
    /// authorization: Bearer のアクセストークンを確かめ、AuthContext を ServiceContext.Items に入れる。
    /// [SkipAuth] の RPC は通さない。無効・期限切れは UNAUTHENTICATED(クライアントはトークンを取り直して 1 回だけ再送する)。
    /// </summary>
    internal sealed class AuthFilter : IMagicOnionServiceFilter
    {
        private const string BearerPrefix = "Bearer ";

        private readonly NpgsqlDataSource dataSource;
        private readonly TimeProvider timeProvider;
        private readonly ILogger<AuthFilter> logger;

        public AuthFilter(NpgsqlDataSource dataSource, TimeProvider timeProvider, ILogger<AuthFilter> logger)
        {
            this.dataSource = dataSource;
            this.timeProvider = timeProvider;
            this.logger = logger;
        }

        public async ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
        {
            if (RpcSettings.Get(context).SkipAuth)
            {
                await next(context);
                return;
            }

            var authContext = await AuthenticateAsync(context);
            context.Items[ContextItems.AuthContext] = authContext;
            RpcLogScope.Find(context)?.SetPlayerId(authContext.PlayerId);

            await next(context);
        }

        private async ValueTask<AuthContext> AuthenticateAsync(ServiceContext context)
        {
            var header = context.CallContext.RequestHeaders.GetValue(ApiHeaders.Authorization);
            if (header == null || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
            {
                logger.LogWarning("authentication failed {reason}", "missing_token");
                throw new ReturnStatusException(StatusCode.Unauthenticated, "missing access token");
            }

            var token = header.Substring(BearerPrefix.Length);
            var cancellationToken = context.CallContext.CancellationToken;
            var row = await DeviceStore.FindTokenAsync(dataSource, token, cancellationToken);
            if (row == null)
            {
                // 発行し直す前の古いトークンもここに来る(発行し直すと行を上書きするため)
                logger.LogInformation("authentication failed {reason}", "unknown_token");
                throw InvalidToken();
            }

            if (row.ExpiresAt <= timeProvider.GetUtcNow())
            {
                // 期限切れは、クライアントがトークンを取り直す前の普段の動作でも起きるので warn にしない
                logger.LogInformation("authentication failed {reason} {device_id}", "expired_token", row.DeviceId);
                throw InvalidToken();
            }

            var resolver = context.ServiceProvider.GetRequiredService<IPlayerResolver>();
            var status = await resolver.ResolveAsync(row.PlayerId, cancellationToken);
            return new AuthContext(row.DeviceId, row.PlayerId, status == PlayerStatus.Active);
        }

        private static ReturnStatusException InvalidToken()
        {
            return new ReturnStatusException(StatusCode.Unauthenticated, "invalid or expired access token");
        }
    }
}
