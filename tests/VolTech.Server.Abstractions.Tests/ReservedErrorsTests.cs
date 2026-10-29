using System.Reflection;
using VolTech.Server.Abstractions;

namespace VolTech.Server.Abstractions.Tests
{
    /// <summary>
    /// 予約エラーの domain と reason は、クライアントの VolTech.Client.ReservedErrors と同じ文字列でなければならない。
    /// 変えると古いアプリがエラーを見分けられなくなるので、値を固定して確かめる。
    /// </summary>
    public class ReservedErrorsTests
    {
        [Fact]
        public void Codes_MatchTheClient()
        {
            Assert.Equal("voltech", ReservedErrors.Domain);
            Assert.Equal(new ErrorCode("voltech", "INTERNAL_ERROR"), ReservedErrors.InternalError);
            Assert.Equal(new ErrorCode("voltech", "DATE_CHANGED"), ReservedErrors.DateChanged);
            Assert.Equal(new ErrorCode("voltech", "MAINTENANCE"), ReservedErrors.Maintenance);
            Assert.Equal(new ErrorCode("voltech", "UPDATE_REQUIRED"), ReservedErrors.UpdateRequired);
            Assert.Equal(new ErrorCode("voltech", "ACCOUNT_SUSPENDED"), ReservedErrors.AccountSuspended);
            Assert.Equal(new ErrorCode("voltech", "NETWORK_ERROR"), ReservedErrors.NetworkError);
            Assert.Equal(new ErrorCode("voltech", "UNKNOWN_ERROR"), ReservedErrors.UnknownError);
            Assert.Equal(new ErrorCode("voltech", "CLIENT_ERROR"), ReservedErrors.ClientError);
            Assert.Equal(new ErrorCode("voltech", "ACCOUNT_DATA_CORRUPTED"), ReservedErrors.AccountDataCorrupted);
            Assert.Equal(new ErrorCode("voltech", "ACCOUNT_SAVE_FAILED"), ReservedErrors.AccountSaveFailed);
        }

        /// <summary>
        /// 予約エラーを足したら、このテストが失敗する。docs/spec/errors.md の予約エラーの表(既定の action)と、
        /// クライアントの ErrorPolicyProvider の既定の action を一緒に直してから、ここに足す。
        /// </summary>
        [Fact]
        public void All_AreListedInTheSpec()
        {
            var listedInSpec = new[]
            {
                "INTERNAL_ERROR", "DATE_CHANGED", "MAINTENANCE", "UPDATE_REQUIRED", "ACCOUNT_SUSPENDED",
                "NETWORK_ERROR", "UNKNOWN_ERROR", "CLIENT_ERROR", "ACCOUNT_DATA_CORRUPTED", "ACCOUNT_SAVE_FAILED",
            };

            var defined = typeof(ReservedErrors)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(ErrorCode))
                .Select(field => (ErrorCode)field.GetValue(null)!)
                .ToArray();

            Assert.All(defined, code => Assert.Equal(ReservedErrors.Domain, code.Domain));
            Assert.Equal(listedInSpec.OrderBy(reason => reason), defined.Select(code => code.Reason).OrderBy(reason => reason));
        }
    }
}
