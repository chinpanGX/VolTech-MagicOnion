using Grpc.Core;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// アプリのプラットフォームとバージョン。リクエストのヘッダーに付ける。今のサーバーはログにだけ使い、強制アップデートの判定を足したらその判定に使う。
    /// </summary>
    public sealed class AppVersion
    {
        /// <summary>iOS。</summary>
        public const string Ios = "ios";

        /// <summary>Android。</summary>
        public const string Android = "android";

        /// <summary>Windows。</summary>
        public const string Windows = "windows";

        /// <summary>Unity のエディタ。サーバーは強制アップデートを判定しない。</summary>
        public const string Editor = "editor";

        /// <summary>
        /// プラットフォーム(Ios・Android・Windows・Editor のどれか)と、整数のバージョンを受け取って作る。
        /// </summary>
        public AppVersion(string platform, int version)
        {
            Platform = platform;
            Version = version;
        }

        /// <summary>プラットフォーム。</summary>
        public string Platform { get; }

        /// <summary>
        /// アプリのバージョン。表示用の「1.2.0」ではなく、リリースのたびに増やす整数(iOS の Build、Android の versionCode と同じ値)。
        /// 整数にするのは、サーバーが比べ間違えないようにするため。
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// プラットフォームとバージョンをリクエストのヘッダーに付ける。
        /// </summary>
        internal void AddHeadersTo(Metadata headers)
        {
            headers.Add(ApiHeaders.AppPlatform, Platform);
            headers.Add(ApiHeaders.AppVersion, Version.ToString());
        }
    }
}
