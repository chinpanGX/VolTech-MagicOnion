using System;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using MagicOnion;
using UnityEditor;
using UnityEngine;
using VolTech.Client.Unity;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// サンプルサーバーの時刻を進める・元に戻すウィンドウ。Play モード中だけ操作できる。
    /// </summary>
    public sealed class ServerClockWindow : EditorWindow
    {
        private const long SecondsPerDay = 24 * 60 * 60;
        private const int DeadlineSeconds = 5;

        private ServerClockInfo info;
        private string status = "";
        private bool busy;

        [MenuItem("VolTech/Server Clock")]
        private static void Open()
        {
            GetWindow<ServerClockWindow>("Server Clock");
        }

        // RPC ごとに締め切りを作る。締め切りは絶対時刻なので、クライアントを使い回さない
        private static IServerClockService WithDeadline(IServerClockService client)
        {
            return client.WithOptions(new CallOptions(deadline: DateTime.UtcNow.AddSeconds(DeadlineSeconds)));
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // ドメインのリロードを切っているので、Play の開始ではここは動かない。Play 中に開いたときの分
            if (EditorApplication.isPlaying)
            {
                RunAsync(GetAsync).Forget();
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                RunAsync(GetAsync).Forget();
            }
        }

        private void OnGUI()
        {
            if (info != null)
            {
                EditorGUILayout.LabelField("サーバーの時刻(UTC)", info.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                EditorGUILayout.LabelField("ゲーム内の日付", info.GameDate);
                EditorGUILayout.LabelField("ずらしている秒数", info.OffsetSeconds.ToString());
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Play モード中に操作できます", MessageType.Info);
            }

            if (status.Length > 0)
            {
                EditorGUILayout.HelpBox(status, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(busy || !EditorApplication.isPlaying))
            {
                if (GUILayout.Button("+1 日"))
                {
                    RunAsync(AdvanceOneDayAsync).Forget();
                }

                if (GUILayout.Button("リセット"))
                {
                    RunAsync(ResetAsync).Forget();
                }

                if (GUILayout.Button("更新"))
                {
                    RunAsync(GetAsync).Forget();
                }
            }
        }

        private static async UniTask<ServerClockInfo> GetAsync(IServerClockService client)
        {
            return await WithDeadline(client).GetAsync();
        }

        private static async UniTask<ServerClockInfo> ResetAsync(IServerClockService client)
        {
            return await WithDeadline(client).SetOffsetAsync(0);
        }

        private static async UniTask<ServerClockInfo> AdvanceOneDayAsync(IServerClockService client)
        {
            var current = await WithDeadline(client).GetAsync();
            return await WithDeadline(client).SetOffsetAsync(current.OffsetSeconds + SecondsPerDay);
        }

        // ボタンを押したときにしか通信しないので、チャンネルの寿命を持たず、通信のたびに作って閉じる
        private async UniTaskVoid RunAsync(Func<IServerClockService, UniTask<ServerClockInfo>> call)
        {
            busy = true;
            status = "";
            Repaint();
            try
            {
                using var channel = ApiChannel.Create(SampleClients.Address);
                info = await call(SampleClients.CreateClockClient(ApiChannel.CreateCallInvoker(channel)));
            }
            catch (RpcException exception)
            {
                status = $"失敗しました: {exception.StatusCode}";
            }

            busy = false;
            if (this != null)
            {
                Repaint();
            }
        }
    }
}
