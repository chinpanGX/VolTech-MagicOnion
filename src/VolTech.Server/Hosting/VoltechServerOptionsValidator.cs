using Microsoft.Extensions.Options;

namespace VolTech.Server.Hosting
{
    /// <summary>
    /// 設定の書き間違いに、起動したときに気づけるようにする。
    /// </summary>
    internal sealed class VoltechServerOptionsValidator : IValidateOptions<VoltechServerOptions>
    {
        public ValidateOptionsResult Validate(string? name, VoltechServerOptions options)
        {
            var failures = new List<string>();
            if (options.GameDay.ResetHour < 0 || options.GameDay.ResetHour > 23)
            {
                failures.Add($"GameDay.ResetHour は 0〜23 にする({options.GameDay.ResetHour})。");
            }

            if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.GameDay.TimeZone, out _))
            {
                failures.Add($"GameDay.TimeZone \"{options.GameDay.TimeZone}\" が見つからない。");
            }

            if (options.AccessTokenLifetime <= TimeSpan.Zero)
            {
                failures.Add($"AccessTokenLifetime は 0 より長くする({options.AccessTokenLifetime})。");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
