using System;

namespace VolTech.Client.Auth
{
    /// <summary>
    /// 保存したアカウント情報のファイルはあるのに、読めない(壊れている・書き換えられている・値が空)ことを表す。
    /// </summary>
    /// <remarks>
    /// 新しいアカウントを勝手に作ると元のデータに戻れなくなるので、基盤は登録し直さない。
    /// アプリは、問い合わせ(player_id が分からないので、端末の情報など)か引き継ぎに誘導するか、
    /// ユーザーが選んだときだけ ClearAsync してから新しく始める。
    /// </remarks>
    public sealed class CorruptedAccountDataException : Exception
    {
        /// <summary>
        /// 読めなかったファイルと、読めなかった理由を受け取って作る。
        /// </summary>
        /// <param name="filePath">読めなかったファイルのパス。</param>
        /// <param name="innerException">読めなかった理由(復号・Mac の照合・パースの失敗)。値が空のときは null。</param>
        public CorruptedAccountDataException(string filePath, Exception innerException)
            : base($"Saved account data is unreadable: {filePath}", innerException)
        {
            FilePath = filePath;
        }

        /// <summary>読めなかったファイルのパス。</summary>
        public string FilePath { get; }
    }
}
