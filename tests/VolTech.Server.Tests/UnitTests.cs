using System.Text.Json.Nodes;
using VolTech.Server.Auth;
using VolTech.Server.Hosting;
using VolTech.Server.Internal;
using VolTech.Server.Status;
using VolTech.Server.Tests.Support;
using VolTech.Server.Transaction;

namespace VolTech.Server.Tests
{
    public class GameClockTests
    {
        /// <summary>ゲーム内の日付は、タイムゾーンの時刻で ResetHour 時に変わる(4 時の直前は前の日)。</summary>
        [Theory]
        [InlineData("2026-10-06T03:59:59+09:00", "2026-10-05")]
        [InlineData("2026-10-06T04:00:00+09:00", "2026-10-06")]
        [InlineData("2026-10-06T23:59:59+09:00", "2026-10-06")]
        [InlineData("2026-10-05T19:30:00+00:00", "2026-10-06")]
        public void GameDate_ChangesAtResetHour(string now, string expected)
        {
            var gameDay = new GameDayOptions { ResetHour = 4, TimeZone = "Asia/Tokyo" };

            var date = GameClock.GetGameDate(DateTimeOffset.Parse(now), gameDay);

            Assert.Equal(expected, date.ToString("yyyy-MM-dd"));
        }
    }

    public class OptionsValidatorTests
    {
        [Fact]
        public void Defaults_AreValid()
        {
            var result = new VoltechServerOptionsValidator().Validate(null, new VoltechServerOptions());

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void InvalidValues_AreRejected()
        {
            var options = new VoltechServerOptions
            {
                GameDay = new GameDayOptions { ResetHour = 24, TimeZone = "Asia/Tokio" },
                AccessTokenLifetime = TimeSpan.Zero,
            };

            var result = new VoltechServerOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Equal(3, result.Failures!.Count());
        }
    }

    public class SecretHasherTests
    {
        [Fact]
        public void Verify_AcceptsOnlyTheSameSecret()
        {
            var stored = SecretHasher.Hash("0123456789abcdef");

            Assert.StartsWith("pbkdf2-sha256$", stored);
            Assert.True(SecretHasher.Verify("0123456789abcdef", stored));
            Assert.False(SecretHasher.Verify("0123456789abcdeg", stored));
            Assert.False(SecretHasher.Verify("0123456789abcdef", "unknown$1$AA==$AA=="));
        }

        [Fact]
        public void Hash_UsesRandomSalt()
        {
            Assert.NotEqual(SecretHasher.Hash("0123456789abcdef"), SecretHasher.Hash("0123456789abcdef"));
        }
    }

    public class GameLogTests
    {
        [Fact]
        public void Body_HasOnlyPresentValues()
        {
            var quantity = GameLog.Quantity("gems", -300, 1200, "gacha_draw").Target("gacha-1").Field("draws", 10);
            var evt = GameLog.Event("unit", "gacha_draw");

            Assert.Equal(
                "{\"target_id\":\"gacha-1\",\"delta\":-300,\"after\":1200,\"reason\":\"gacha_draw\",\"draws\":10}",
                quantity.ToBody().ToJsonString());
            Assert.Equal("{\"reason\":\"gacha_draw\"}", evt.ToBody().ToJsonString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("Upper")]
        [InlineData("has space")]
        [InlineData("player_id")]
        [InlineData("kind")]
        public void Field_RejectsInvalidNames(string name)
        {
            var log = GameLog.Event("unit", "test");

            Assert.Throws<ArgumentException>(() => log.Field(name, JsonValue.Create(1)));
        }

        [Fact]
        public void Field_RejectsDuplicates()
        {
            var log = GameLog.Event("unit", "test").Field("unit_id", 1);

            Assert.Throws<ArgumentException>(() => log.Field("unit_id", 2));
        }
    }

    public class RpcSettingsTests
    {
        /// <summary>共有インターフェースに付けた属性が、実装クラスのメソッドから読める。</summary>
        [Fact]
        public void Settings_ComeFromInterfaceAttributes()
        {
            var service = typeof(PlayerTestService);

            var whoAmI = RpcSettings.Create(service, service.GetMethod(nameof(PlayerTestService.WhoAmIAsync))!, "x");
            var addGems = RpcSettings.Create(service, service.GetMethod(nameof(PlayerTestService.AddGemsAsync))!, "x");
            var publicMethod = RpcSettings.Create(
                typeof(PublicTestService),
                typeof(PublicTestService).GetMethod(nameof(PublicTestService.ThrowBusinessAsync))!,
                "x");

            Assert.True(whoAmI.NoSideEffects);
            Assert.False(whoAmI.SkipAuth);
            Assert.False(addGems.NoSideEffects);
            Assert.True(publicMethod.SkipAuth);
        }
    }
}
