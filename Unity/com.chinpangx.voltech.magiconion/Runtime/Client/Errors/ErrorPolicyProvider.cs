using System;
using System.Collections.Generic;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 基盤の予約エラー(ReservedErrors)と、アプリの業務エラーの一覧から、既定の action を引く。
    /// </summary>
    /// <example>
    /// <code>new ErrorPolicyProvider(AppErrors.All)</code>
    /// </example>
    public sealed class ErrorPolicyProvider : IErrorPolicyProvider
    {
        private readonly Dictionary<ErrorCode, ErrorAction> defaultActions = new();

        /// <summary>
        /// アプリのエラーの一覧を受け取って作る。基盤の予約エラーは自動で入る。
        /// </summary>
        /// <exception cref="ArgumentException">アプリのエラーが予約エラーの domain を使っている、または重複している。</exception>
        public ErrorPolicyProvider(IEnumerable<ErrorDefinition> appErrors)
        {
            AddReservedErrors();

            foreach (var definition in appErrors ?? Array.Empty<ErrorDefinition>())
            {
                // 予約エラーを上書きさせない
                if (ErrorConverter.IsReserved(definition.Code))
                {
                    throw new ArgumentException($"アプリのエラーに予約エラーの domain は使えない: {definition.Code}", nameof(appErrors));
                }

                if (!defaultActions.TryAdd(definition.Code, definition.DefaultAction))
                {
                    throw new ArgumentException($"エラーコードが重複している: {definition.Code}", nameof(appErrors));
                }
            }
        }

        // 予約エラーの既定の action。VolTech-gRPC 版では errors.toml から生成した ReservedErrors.All に入っていた。
        // static な一覧は持たない(Unity 7 は static フィールドに属性を求める。UAL0010)
        private void AddReservedErrors()
        {
            defaultActions.Add(ReservedErrors.NetworkError, ErrorAction.Retry);
            defaultActions.Add(ReservedErrors.InternalError, ErrorAction.Retry);
            defaultActions.Add(ReservedErrors.UnknownError, ErrorAction.Title);
            defaultActions.Add(ReservedErrors.Maintenance, ErrorAction.Title);
            defaultActions.Add(ReservedErrors.UpdateRequired, ErrorAction.Update);
            defaultActions.Add(ReservedErrors.AccountSuspended, ErrorAction.Title);
            defaultActions.Add(ReservedErrors.DateChanged, ErrorAction.Title);
            defaultActions.Add(ReservedErrors.ClientError, ErrorAction.Title);
            defaultActions.Add(ReservedErrors.AccountDataCorrupted, ErrorAction.Stay);
            defaultActions.Add(ReservedErrors.AccountSaveFailed, ErrorAction.Stay);
        }

        /// <summary>
        /// 知っているエラーコードかを返す。
        /// </summary>
        public bool IsKnown(ErrorCode code)
        {
            return defaultActions.ContainsKey(code);
        }

        /// <summary>
        /// エラーコードの既定の action を返す。知らないコードは Title。
        /// </summary>
        public ErrorAction GetDefaultAction(ErrorCode code)
        {
            return defaultActions.TryGetValue(code, out var action) && action != ErrorAction.Unspecified
                ? action
                : ErrorAction.Title;
        }
    }
}
