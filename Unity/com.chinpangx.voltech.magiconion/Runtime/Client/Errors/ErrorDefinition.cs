using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// エラーコードと、その既定の action。アプリは業務エラーごとに作り、一覧を ErrorPolicyProvider に渡す。
    /// </summary>
    public readonly struct ErrorDefinition
    {
        /// <summary>
        /// エラーコードと既定の action から作る。
        /// </summary>
        public ErrorDefinition(ErrorCode code, ErrorAction defaultAction)
        {
            Code = code;
            DefaultAction = defaultAction;
        }

        /// <summary>エラーコード。</summary>
        public ErrorCode Code { get; }

        /// <summary>既定の action。サーバーが metadata["action"] で上書きしなければ、これを使う。</summary>
        public ErrorAction DefaultAction { get; }
    }
}
