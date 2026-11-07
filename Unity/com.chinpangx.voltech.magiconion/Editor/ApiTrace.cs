using System;
using System.Collections.Generic;

namespace VolTech.Client.Editor
{
    /// <summary>
    /// 1 回の通信の記録。Monitor(ApiMonitorWindow)が表示する。
    /// </summary>
    /// <remarks>
    /// ApiTraceStore に入れた後は書き換えない(別のスレッドから読まれるため)。状態が変わったら、同じ Id の新しい記録で置き換える。
    /// Body は通信の流れの中で JSON にせず、読まれたときに初めて JSON にする(SetRequestBody・SetResponseBody)。
    /// </remarks>
    internal sealed class ApiTrace
    {
        /// <summary>記録の連番。送信中の記録と、それを埋めた記録は同じ Id。</summary>
        public int Id;

        /// <summary>RPC のフルパス(例: /ISampleGachaService/DrawAsync)。</summary>
        public string Method;

        /// <summary>送り始めた時刻(端末の時計の、端末のタイムゾーン)。ISO 8601 の文字列。</summary>
        public string StartedAt;

        /// <summary>送り始めてから結果を受け取るまでのミリ秒。送信中は -1。</summary>
        public double DurationMs = -1;

        /// <summary>締め切り(UTC)。ISO 8601 の文字列。無ければ空。</summary>
        public string Deadline;

        /// <summary>状態。</summary>
        public ApiTraceState State;

        /// <summary>送ったヘッダー(送った順)。</summary>
        public List<ApiTraceHeader> RequestHeaders = new();

        /// <summary>受け取ったヘッダー。</summary>
        public List<ApiTraceHeader> ResponseHeaders = new();

        /// <summary>受け取ったトレーラー。</summary>
        public List<ApiTraceHeader> Trailers = new();

        /// <summary>gRPC のステータスコードの名前(例: OK、FailedPrecondition)。送信中は空。</summary>
        public string StatusCode;

        /// <summary>ステータスの detail(サーバーのメッセージ)。</summary>
        public string StatusDetail;

        /// <summary>ErrorInfo.reason(例: NOT_ENOUGH_GEMS)。無ければ空。</summary>
        public string ErrorReason;

        /// <summary>ErrorInfo.domain(例: voltech)。</summary>
        public string ErrorDomain;

        /// <summary>ErrorInfo.metadata。</summary>
        public List<ApiTraceHeader> ErrorMetadata = new();

        private Lazy<string> requestBody;
        private Lazy<string> responseBody;

        /// <summary>送った Body(JSON)。最初に読むときに JSON にする。</summary>
        public string RequestBody => requestBody?.Value;

        /// <summary>受け取った Body(JSON)。エラーのときは空。最初に読むときに JSON にする。</summary>
        public string ResponseBody => responseBody?.Value;

        /// <summary>RPC のフルパスから、サービス名を省いた短い名前(例: DrawAsync)。</summary>
        public string ShortMethod => Method?.Substring(Method.LastIndexOf('/') + 1) ?? "";

        /// <summary>
        /// 送った Body を、読まれたときに JSON にするように入れる。
        /// </summary>
        public void SetRequestBody(Lazy<string> json)
        {
            requestBody = json;
        }

        /// <summary>
        /// 受け取った Body を、読まれたときに JSON にするように入れる。
        /// </summary>
        public void SetResponseBody(Lazy<string> json)
        {
            responseBody = json;
        }

        /// <summary>
        /// 同じ中身の複製を作る。記録を書き換えずに、状態を変えた新しい記録を作るときに使う。
        /// </summary>
        public ApiTrace Clone()
        {
            var clone = (ApiTrace)MemberwiseClone();
            clone.RequestHeaders = new List<ApiTraceHeader>(RequestHeaders);
            clone.ResponseHeaders = new List<ApiTraceHeader>(ResponseHeaders);
            clone.Trailers = new List<ApiTraceHeader>(Trailers);
            clone.ErrorMetadata = new List<ApiTraceHeader>(ErrorMetadata);
            return clone;
        }
    }

    /// <summary>
    /// 通信の状態。
    /// </summary>
    internal enum ApiTraceState
    {
        /// <summary>応答を待っている。</summary>
        Pending,

        /// <summary>成功した。</summary>
        Succeeded,

        /// <summary>エラーになった。</summary>
        Failed,
    }

    /// <summary>
    /// ヘッダー・トレーラー・ErrorInfo.metadata の 1 項目。
    /// </summary>
    internal sealed class ApiTraceHeader
    {
        /// <summary>
        /// 名前と値を受け取って作る。
        /// </summary>
        public ApiTraceHeader(string key, string value)
        {
            Key = key;
            Value = value;
        }

        /// <summary>名前。</summary>
        public string Key { get; }

        /// <summary>値。バイナリ(-bin)は、バイト数と base64。</summary>
        public string Value { get; }
    }
}
