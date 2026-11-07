using Cysharp.Threading.Tasks;
using UnityEngine;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// ガチャの画面。サンプルサーバーを起動してから Play モードにする。動かし方は docs/designs/sandbox-gacha-date-sample.md。
    /// </summary>
    public sealed class SampleGame : MonoBehaviour
    {
        private SampleUi ui;
        private SampleSession session;
        private bool busy;
        private bool atHome;

        private void Awake()
        {
            ui = new SampleUi(transform);
            var view = new SampleErrorView(ui.Band, ui.BandText, ui.BandFirstButton, ui.BandSecondButton);
            session = SampleSetup.Create(SampleClients.Address, view, new SampleNavigator(ShowTitle));
            ui.StartButton.onClick.AddListener(() => EnterAsync().Forget());
            ui.FreeButton.onClick.AddListener(() => DrawAsync(useFree: true).Forget());
            ui.PaidButton.onClick.AddListener(() => DrawAsync(useFree: false).Forget());
            ShowTitle();
        }

        private void OnDestroy()
        {
            session.Dispose();
        }

        private void ShowTitle()
        {
            atHome = false;
            ui.ResultText.text = "";
            Refresh();
        }

        private async UniTaskVoid EnterAsync()
        {
            SetBusy(true);
            var result = await session.EnterAsync();
            atHome = !result.IsError;
            SetBusy(false);
        }

        private async UniTaskVoid DrawAsync(bool useFree)
        {
            SetBusy(true);
            var result = await session.DrawAsync(useFree);
            if (!result.IsError)
            {
                ui.ResultText.text = $"{result.Value.CharacterName} が当たりました";
            }
            else if (result.ErrorCode == SampleErrors.NotEnoughGems)
            {
                ui.ResultText.text = "ジェムが足りません";
            }
            else if (result.ErrorCode == SampleErrors.FreeDrawUsed)
            {
                ui.ResultText.text = "今日の無料ガチャは引きました";
            }

            SetBusy(false);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            Refresh();
        }

        private void Refresh()
        {
            ui.Title.SetActive(!atHome);
            ui.Home.SetActive(atHome);
            ui.StartButton.interactable = !busy;
            ui.PaidButton.interactable = !busy;
            ui.FreeButton.interactable = !busy && session.Player.FreeDrawAvailable;
            ui.GemsText.text = $"{session.Player.Gems} ジェム";
        }
    }
}
