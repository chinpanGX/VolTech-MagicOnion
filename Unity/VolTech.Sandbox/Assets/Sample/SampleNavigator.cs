using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VolTech.Client;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// エラーのときの画面の遷移。トークンと日付は ApiCore が捨てるので、ここは画面を戻すだけ。
    /// </summary>
    public sealed class SampleNavigator : IErrorNavigator
    {
        private readonly Action goToTitle;

        public SampleNavigator(Action goToTitle)
        {
            this.goToTitle = goToTitle;
        }

        public UniTask GoToTitleAsync()
        {
            goToTitle();
            return UniTask.CompletedTask;
        }

        public UniTask GoToHomeAsync()
        {
            Debug.Log("このサンプルでは Home の action は起きない");
            return UniTask.CompletedTask;
        }

        public UniTask OpenStoreAsync()
        {
            Debug.Log("このサンプルでは Update の action は起きない");
            return UniTask.CompletedTask;
        }
    }
}
