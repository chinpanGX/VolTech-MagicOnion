using System;
using System.IO;
using Cysharp.Threading.Tasks;

namespace VolTech.Client
{
    /// <summary>
    /// 一時ファイルに書き込んでから対象のファイルと置き換えることで、書き込み途中でプロセスが
    /// クラッシュしても対象のファイルが中途半端な内容にならないようにする。
    /// </summary>
    /// <remarks>
    /// 自作パッケージ Supplement の SafeFile を、パッケージとしては依存せずにコピーしたもの。
    /// </remarks>
    internal static class SafeFile
    {
        private const string TempFileInfix = ".tmp";
        private const string TempFileSuffix = "~";

        // リトライの待ちは実時間で数える。Time.timeScale が 0 の間(一時停止中)に保存・削除しても、待ちが終わらなくならないようにするため
        private const int MaxRetryCount = 10;
        private const int InitialRetryDelayMilliseconds = 100;
        private const int MaxRetryDelayMilliseconds = 5000;

        internal static async UniTask WriteAllBytesAsync(string filePath, byte[] bytes)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            CleanUpStaleTempFiles(filePath, dir);

            var tempPath = $"{filePath}{TempFileInfix}{Guid.NewGuid():N}{TempFileSuffix}";
            await WriteTempFileAsync(tempPath, bytes);
            await ReplaceAsync(tempPath, filePath);
        }

        internal static async UniTask DeleteAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            // 削除対象を他プロセス(セキュリティソフト等)が一時的に開いている可能性があるためリトライする
            var delay = InitialRetryDelayMilliseconds;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Delete(filePath);
                    return;
                }
                catch (IOException) when (attempt < MaxRetryCount)
                {
                    await UniTask.Delay(delay, DelayType.Realtime);
                    delay = Math.Min(delay * 2, MaxRetryDelayMilliseconds);
                }
            }
        }

        private static async UniTask WriteTempFileAsync(string tempPath, byte[] bytes)
        {
            // セキュリティソフト等による一時的なファイルロックの可能性があるためリトライする。
            // ディスク容量不足等それ以外の原因の IOException はリトライしても解決しないためそのまま投げる
            var delay = InitialRetryDelayMilliseconds;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // Task は AsUniTask() を通さずにそのまま await する(本体の決まり)
                    await File.WriteAllBytesAsync(tempPath, bytes);
                    return;
                }
                catch (UnauthorizedAccessException) when (attempt < MaxRetryCount)
                {
                    await UniTask.Delay(delay, DelayType.Realtime);
                    delay = Math.Min(delay * 2, MaxRetryDelayMilliseconds);
                }
            }
        }

        private static async UniTask ReplaceAsync(string tempPath, string filePath)
        {
            // 置き換え先を他プロセス(セキュリティソフト等)が一時的に開いている可能性があるためリトライする
            var delay = InitialRetryDelayMilliseconds;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Replace(tempPath, filePath, null);
                    }
                    else
                    {
                        File.Move(tempPath, filePath);
                    }

                    return;
                }
                catch (IOException) when (attempt < MaxRetryCount)
                {
                    await UniTask.Delay(delay, DelayType.Realtime);
                    delay = Math.Min(delay * 2, MaxRetryDelayMilliseconds);
                }
            }
        }

        // 前回の書き込み中のクラッシュ等で残った一時ファイルを消す。消せなくても今回の書き込みには影響しないので、失敗は無視する
        private static void CleanUpStaleTempFiles(string filePath, string dir)
        {
            var searchDir = string.IsNullOrEmpty(dir) ? "." : dir;
            if (!Directory.Exists(searchDir))
            {
                return;
            }

            var searchPattern = $"{Path.GetFileName(filePath)}{TempFileInfix}*{TempFileSuffix}";
            foreach (var stalePath in Directory.EnumerateFiles(searchDir, searchPattern))
            {
                try
                {
                    File.Delete(stalePath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
