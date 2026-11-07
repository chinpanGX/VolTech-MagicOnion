using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VolTech.Client;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// エラーの帯。文言とボタンを出し、押されるまで待つ。
    /// </summary>
    public sealed class SampleErrorView : IErrorDialogView
    {
        private readonly GameObject band;
        private readonly Text message;
        private readonly Button firstButton;
        private readonly Button secondButton;

        public SampleErrorView(GameObject band, Text message, Button firstButton, Button secondButton)
        {
            this.band = band;
            this.message = message;
            this.firstButton = firstButton;
            this.secondButton = secondButton;
        }

        public async UniTask<ErrorDialogButton> ShowAsync(ApiError error, ErrorDialogButtons buttons)
        {
            // ErrorMessage はユーザーに見せない決まりなので、文言は画面が持つ
            message.text = MessageOf(error);
            var source = new UniTaskCompletionSource<ErrorDialogButton>();
            switch (buttons)
            {
                case ErrorDialogButtons.Ok:
                    Configure(firstButton, ErrorDialogButton.Ok, source);
                    Hide(secondButton);
                    break;
                case ErrorDialogButtons.RetryOrTitle:
                    Configure(firstButton, ErrorDialogButton.Retry, source);
                    Configure(secondButton, ErrorDialogButton.Title, source);
                    break;
                case ErrorDialogButtons.Home:
                    Configure(firstButton, ErrorDialogButton.Home, source);
                    Hide(secondButton);
                    break;
                case ErrorDialogButtons.Store:
                    Configure(firstButton, ErrorDialogButton.Store, source);
                    Hide(secondButton);
                    break;
                default:
                    Configure(firstButton, ErrorDialogButton.Title, source);
                    Hide(secondButton);
                    break;
            }

            band.SetActive(true);
            var pressed = await source.Task;
            band.SetActive(false);
            return pressed;
        }

        private static void Configure(Button button, ErrorDialogButton kind, UniTaskCompletionSource<ErrorDialogButton> source)
        {
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<Text>().text = LabelOf(kind);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => source.TrySetResult(kind));
        }

        private static void Hide(Button button)
        {
            button.onClick.RemoveAllListeners();
            button.gameObject.SetActive(false);
        }

        private static string MessageOf(ApiError error)
        {
            if (error.Code == ReservedErrors.DateChanged)
            {
                return "日付が変わりました";
            }

            return error.Code == ReservedErrors.NetworkError ? "サーバーに接続できません" : "エラーが発生しました";
        }

        private static string LabelOf(ErrorDialogButton kind)
        {
            switch (kind)
            {
                case ErrorDialogButton.Retry:
                    return "再試行";
                case ErrorDialogButton.Title:
                    return "タイトルへ";
                case ErrorDialogButton.Home:
                    return "ホームへ";
                case ErrorDialogButton.Store:
                    return "ストアへ";
                default:
                    return "OK";
            }
        }
    }
}
