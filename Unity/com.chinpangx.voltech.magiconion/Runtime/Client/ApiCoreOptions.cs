using System;

namespace VolTech.Client
{
    /// <summary>
    /// ApiCore の設定。
    /// </summary>
    public sealed class ApiCoreOptions
    {
        /// <summary>
        /// 設定を受け取って作る。
        /// </summary>
        /// <param name="appVersion">アプリのプラットフォームとバージョン。null ならヘッダーに付けない(サーバーは強制アップデートを判定しない)。</param>
        public ApiCoreOptions(AppVersion appVersion = null)
        {
            AppVersion = appVersion;
        }

        /// <summary>1回の通信全体の制限時間。全 RPC 共通。</summary>
        public TimeSpan Deadline { get; } = TimeSpan.FromSeconds(15);

        /// <summary>アプリのプラットフォームとバージョン。</summary>
        public AppVersion AppVersion { get; }
    }
}
