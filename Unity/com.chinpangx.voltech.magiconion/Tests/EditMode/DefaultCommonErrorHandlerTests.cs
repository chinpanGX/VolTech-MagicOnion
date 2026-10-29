using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Client;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// 共通エラーハンドラーの既定の実装が、action ごとに決まったダイアログを出し、押されたボタンから動きを決めることを確かめる。
    /// </summary>
    [Description("共通エラーハンドラーの既定の実装が、action ごとに決まったダイアログを出し、押されたボタンから動きを決めることを確かめる")]
    public class DefaultCommonErrorHandlerTests
    {
        /// <summary>
        /// action ごとに、出すボタン・押したボタン・返す動きが表のとおりになる。画面の遷移は ApiCore が行うので、ハンドラーはしない。
        /// </summary>
        [UnityTest]
        [Description("action ごとに、出すボタン・押したボタン・返す動きが表のとおりになる")]
        public IEnumerator EachActionShowsDialogAndDecides() => UniTask.ToCoroutine(async () =>
        {
            var cases = new (ErrorAction Action, ErrorDialogButton Pressed, ErrorDialogButtons Buttons, ErrorDecision Decision)[]
            {
                (ErrorAction.Retry, ErrorDialogButton.Retry, ErrorDialogButtons.RetryOrTitle, ErrorDecision.Retry),
                (ErrorAction.Retry, ErrorDialogButton.Title, ErrorDialogButtons.RetryOrTitle, ErrorDecision.ReturnToTitle),
                (ErrorAction.Stay, ErrorDialogButton.Ok, ErrorDialogButtons.Ok, ErrorDecision.Stay),
                (ErrorAction.Home, ErrorDialogButton.Home, ErrorDialogButtons.Home, ErrorDecision.ReturnToHome),
                (ErrorAction.Title, ErrorDialogButton.Title, ErrorDialogButtons.Title, ErrorDecision.ReturnToTitle),
                (ErrorAction.Update, ErrorDialogButton.Store, ErrorDialogButtons.Store, ErrorDecision.OpenStore),
            };

            foreach (var (action, pressed, buttons, decision) in cases)
            {
                var dialog = new ScriptedDialog(pressed);
                var handler = new DefaultCommonErrorHandler(dialog);

                var result = await handler.HandleAsync(new ApiError(new ErrorCode("test", "TEST"), action, StatusCode.Unknown, ""));

                var label = $"{action} / {pressed}";
                Assert.That(dialog.ShownButtons, Is.EqualTo(new[] { buttons }), label);
                Assert.That(result, Is.EqualTo(decision), label);
            }
        });

        // 決めておいたボタンを押したことにするダイアログ
        private sealed class ScriptedDialog : IErrorDialogView
        {
            private readonly ErrorDialogButton pressed;

            public ScriptedDialog(ErrorDialogButton pressed)
            {
                this.pressed = pressed;
            }

            public List<ErrorDialogButtons> ShownButtons { get; } = new();

            public UniTask<ErrorDialogButton> ShowAsync(ApiError error, ErrorDialogButtons buttons)
            {
                ShownButtons.Add(buttons);
                return UniTask.FromResult(pressed);
            }
        }
    }
}
