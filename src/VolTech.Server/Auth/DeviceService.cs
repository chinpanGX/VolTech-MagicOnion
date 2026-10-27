using System.Security.Cryptography;
using Grpc.Core;
using MagicOnion;
using MagicOnion.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using VolTech.Server.Abstractions;
using VolTech.Server.Hosting;
using VolTech.Server.Logging;

namespace VolTech.Server.Auth
{
    /// <summary>
    /// デバイス登録とアクセストークンの発行(IDeviceService の実装)。
    /// </summary>
    public sealed class DeviceService : ServiceBase<IDeviceService>, IDeviceService
    {
        private const int MinSecretKeyLength = 16;
        private const int MaxSecretKeyLength = 128;
        private const int AccessTokenBytes = 32;

        private readonly NpgsqlDataSource dataSource;
        private readonly TimeProvider timeProvider;
        private readonly IOptionsMonitor<VoltechServerOptions> options;
        private readonly ILogger<DeviceService> logger;

        public DeviceService(
            NpgsqlDataSource dataSource,
            TimeProvider timeProvider,
            IOptionsMonitor<VoltechServerOptions> options,
            ILogger<DeviceService> logger)
        {
            this.dataSource = dataSource;
            this.timeProvider = timeProvider;
            this.options = options;
            this.logger = logger;
        }

        public async UnaryResult<RegisterResult> RegisterAsync(string secretKey)
        {
            ValidateSecretKey(secretKey);

            var secretHash = SecretHasher.Hash(secretKey);
            var deviceId = Ulid.NewUlid().ToString();
            // ID だけを発行し、プレイヤー(初期データ)はアプリのサインアップで作る
            var playerId = Ulid.NewUlid();
            await DeviceStore.InsertDeviceAsync(dataSource, deviceId, playerId, secretHash, Context.CallContext.CancellationToken);
            RpcLogScope.Find(Context)?.SetPlayerId(playerId);
            logger.LogInformation("device registered {device_id}", deviceId);

            return new RegisterResult
            {
                DeviceId = deviceId,
                PlayerId = playerId.ToString(),
            };
        }

        public async UnaryResult<IssueTokenResult> IssueTokenAsync(string deviceId, string secretKey)
        {
            var cancellationToken = Context.CallContext.CancellationToken;
            var device = await DeviceStore.FindDeviceAsync(dataSource, deviceId ?? "", cancellationToken);
            if (device == null)
            {
                logger.LogWarning("access token issue failed {reason} {device_id}", "unknown_device", deviceId);
                throw InvalidCredentials();
            }

            // 誰のデバイスかは、secretKey を確かめる前からログに付ける(失敗の調査に使うため)
            RpcLogScope.Find(Context)?.SetPlayerId(device.PlayerId);
            if (secretKey == null || !SecretHasher.Verify(secretKey, device.SecretHash))
            {
                logger.LogWarning("access token issue failed {reason} {device_id}", "invalid_secret", deviceId);
                throw InvalidCredentials();
            }

            var accessToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(AccessTokenBytes));
            var expiresAt = TruncateToSeconds(timeProvider.GetUtcNow() + options.CurrentValue.AccessTokenLifetime);
            await DeviceStore.UpsertTokenAsync(dataSource, deviceId!, accessToken, expiresAt, cancellationToken);
            // トークンそのものはログに出さない
            logger.LogInformation("access token issued {device_id}", deviceId);

            return new IssueTokenResult
            {
                AccessToken = accessToken,
                ExpiresAt = expiresAt.UtcDateTime,
            };
        }

        private static void ValidateSecretKey(string? secretKey)
        {
            // ASCII だけにして、文字数とバイト数が食い違わないようにする
            var valid = secretKey != null
                && secretKey.Length >= MinSecretKeyLength
                && secretKey.Length <= MaxSecretKeyLength
                && secretKey.All(character => character >= '!' && character <= '~');
            if (!valid)
            {
                throw new ReturnStatusException(
                    StatusCode.InvalidArgument,
                    $"secretKey must be {MinSecretKeyLength}..{MaxSecretKeyLength} printable ASCII characters (no spaces)");
            }
        }

        private static ReturnStatusException InvalidCredentials()
        {
            return new ReturnStatusException(StatusCode.Unauthenticated, "invalid device credentials");
        }

        private static DateTimeOffset TruncateToSeconds(DateTimeOffset value)
        {
            return DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());
        }
    }
}
