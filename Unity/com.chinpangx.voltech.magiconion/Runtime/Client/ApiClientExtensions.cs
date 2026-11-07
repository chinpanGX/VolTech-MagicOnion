using System;
using System.Linq;
using Grpc.Core;
using MagicOnion;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// ApiCore.CreateClient で作ったクライアントの、呼び出しごとの指定。RPC を呼ぶ前につなぐ。
    /// </summary>
    /// <remarks>
    /// 指定は内部のヘッダーで基盤のフィルターへ渡す。WithHeaders は前の指定を消すので、WithHandledCodes と WithoutActivity を続けてつながない。
    /// 両方が要るときは WithoutActivity にコードを渡す。アプリの WithHeaders・WithOptions も、これらの指定を消す。
    /// </remarks>
    public static class ApiClientExtensions
    {
        // サーバーには送らない(ApiCallInvoker が写さない)ので、クライアントとの取り決めではない。ApiHeaders には入れない
        private const string CallHeader = "x-voltech-call";
        private const string CodesPrefix = "codes=";

        /// <summary>
        /// 呼び出し側が自分で処理するエラーコードを指定する。ダイアログを出さずに失敗として返る。
        /// </summary>
        /// <remarks>
        /// 予約エラーは指定しても共通エラーハンドラーが処理する。
        /// </remarks>
        /// <param name="client">ApiCore.CreateClient で作ったクライアント。</param>
        /// <param name="codes">呼び出し側が処理するエラーコード。</param>
        /// <typeparam name="T">共有インターフェース。</typeparam>
        /// <returns>指定を持つクライアント。変数に持って何度呼んでも効く。</returns>
        public static T WithHandledCodes<T>(this T client, params ErrorCode[] codes)
            where T : IService<T>
        {
            return WithCallSettings(client, codes, notifyActivity: true);
        }

        /// <summary>
        /// 進行中の呼び出しとしてアクティビティのハンドラーに知らせない。画面を止めずに裏で呼ぶものに使う。
        /// </summary>
        /// <remarks>
        /// 変わるのは進行中の通知だけで、エラーは共通エラーハンドラーが処理する。
        /// </remarks>
        /// <param name="client">ApiCore.CreateClient で作ったクライアント。</param>
        /// <param name="codes">呼び出し側が処理するエラーコード。WithHandledCodes と同じ意味。</param>
        /// <typeparam name="T">共有インターフェース。</typeparam>
        /// <returns>指定を持つクライアント。変数に持って何度呼んでも効く。</returns>
        public static T WithoutActivity<T>(this T client, params ErrorCode[] codes)
            where T : IService<T>
        {
            return WithCallSettings(client, codes, notifyActivity: false);
        }

        // 指定を読む。ヘッダーが無ければ、コードの指定なしで進行中を知らせる
        internal static void ReadCallSettings(Metadata headers, out ErrorCode[] handledCodes, out bool notifyActivity)
        {
            handledCodes = null;
            notifyActivity = true;
            var value = headers?.GetValue(CallHeader);
            if (value == null)
            {
                return;
            }

            var parts = value.Split(';');
            if (parts.Length != 2 || (parts[0] != "activity=0" && parts[0] != "activity=1") || !parts[1].StartsWith(CodesPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{CallHeader} の値を読めない: {value}");
            }

            notifyActivity = parts[0] == "activity=1";
            handledCodes = parts[1].Substring(CodesPrefix.Length)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseCode)
                .ToArray();
        }

        private static T WithCallSettings<T>(T client, ErrorCode[] codes, bool notifyActivity)
            where T : IService<T>
        {
            var formatted = string.Join(",", codes.Select(FormatCode));
            var value = $"activity={(notifyActivity ? 1 : 0)};{CodesPrefix}{formatted}";
            return client.WithHeaders(new Metadata { { CallHeader, value } });
        }

        private static string FormatCode(ErrorCode code)
        {
            return $"{Uri.EscapeDataString(code.Domain)}/{Uri.EscapeDataString(code.Reason)}";
        }

        private static ErrorCode ParseCode(string value)
        {
            var parts = value.Split('/');
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"{CallHeader} のエラーコードを読めない: {value}");
            }

            return new ErrorCode(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
        }
    }
}
