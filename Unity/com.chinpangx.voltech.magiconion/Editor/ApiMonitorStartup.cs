using UnityEditor;
using VolTech.Client.Unity;

namespace VolTech.Client.Editor
{
    /// <summary>
    /// Editor の起動時に、通信の記録を始める。ウィンドウを開いていなくても記録する。
    /// </summary>
    internal static class ApiMonitorStartup
    {
        private const string ClearOnPlayKey = "VolTech.MagicOnion.Client.Monitor.ClearOnPlay";

        /// <summary>
        /// Play を始めるときに、通信の記録を消すか。Editor の設定(EditorPrefs)に残す。
        /// </summary>
        public static bool ClearOnPlay
        {
            get => EditorPrefs.GetBool(ClearOnPlayKey, true);
            set => EditorPrefs.SetBool(ClearOnPlayKey, value);
        }

        // スクリプトを読み込むたびに動く。Play にも EditMode のテストにも間に合う
        [InitializeOnLoadMethod]
        private static void Start()
        {
            ApiChannel.SetInterceptor(new ApiTraceInterceptor(ApiTraceStore.Shared));
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // ドメインのリロードを切っているときは記録が残るので、Play を始める前に消す。
        // Play を始めた後(EnteredPlayMode)だと、最初のシーンの初期化で送った通信の記録まで消してしまう。
        // ウィンドウを閉じていても効くように、ウィンドウではなくここで行う
        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && ClearOnPlay)
            {
                ApiTraceStore.Shared.Clear();
            }
        }
    }
}
