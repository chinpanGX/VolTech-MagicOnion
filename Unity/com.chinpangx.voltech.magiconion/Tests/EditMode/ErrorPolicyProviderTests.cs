using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// ErrorPolicyProvider が、すべての予約エラーに、仕様書(docs/spec/errors.md の予約エラーの表)の既定の action を持つことを確かめる。
    /// </summary>
    [Description("ErrorPolicyProvider が、すべての予約エラーに仕様書の既定の action を持つことを確かめる")]
    public class ErrorPolicyProviderTests
    {
        /// <summary>
        /// ReservedErrors の公開フィールドをすべて列挙し、どれにも既定の action があることを確かめる。予約エラーを足して既定の action を入れ忘れると失敗する。
        /// </summary>
        [Test]
        [Description("すべての予約エラーに既定の action がある")]
        public void EveryReservedErrorHasDefaultAction()
        {
            var provider = new ErrorPolicyProvider(Array.Empty<ErrorDefinition>());

            var missing = AllReservedErrors().Where(code => !provider.IsKnown(code)).ToArray();

            Assert.That(missing, Is.Empty);
        }

        /// <summary>
        /// 予約エラーの既定の action が、仕様書の表と同じ。
        /// </summary>
        [Test]
        [Description("予約エラーの既定の action が、仕様書の表と同じ")]
        public void DefaultActionsMatchTheSpec()
        {
            var provider = new ErrorPolicyProvider(Array.Empty<ErrorDefinition>());
            var spec = new Dictionary<ErrorCode, ErrorAction>
            {
                [ReservedErrors.InternalError] = ErrorAction.Retry,
                [ReservedErrors.DateChanged] = ErrorAction.Title,
                [ReservedErrors.Maintenance] = ErrorAction.Title,
                [ReservedErrors.UpdateRequired] = ErrorAction.Update,
                [ReservedErrors.AccountSuspended] = ErrorAction.Title,
                [ReservedErrors.NetworkError] = ErrorAction.Retry,
                [ReservedErrors.UnknownError] = ErrorAction.Title,
                [ReservedErrors.ClientError] = ErrorAction.Title,
                [ReservedErrors.AccountDataCorrupted] = ErrorAction.Stay,
                [ReservedErrors.AccountSaveFailed] = ErrorAction.Stay,
            };

            Assert.That(spec.Keys, Is.EquivalentTo(AllReservedErrors()));
            foreach (var pair in spec)
            {
                Assert.That(provider.GetDefaultAction(pair.Key), Is.EqualTo(pair.Value), pair.Key.ToString());
            }
        }

        /// <summary>
        /// アプリのエラーに予約エラーの domain は使えない。
        /// </summary>
        [Test]
        [Description("アプリのエラーに予約エラーの domain は使えない")]
        public void RejectsAppErrorInReservedDomain()
        {
            var definitions = new[] { new ErrorDefinition(new ErrorCode(ReservedErrors.Domain, "APP_ERROR"), ErrorAction.Stay) };

            Assert.Throws<ArgumentException>(() => new ErrorPolicyProvider(definitions));
        }

        private static IEnumerable<ErrorCode> AllReservedErrors()
        {
            return typeof(ReservedErrors)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(ErrorCode))
                .Select(field => (ErrorCode)field.GetValue(null));
        }
    }
}
