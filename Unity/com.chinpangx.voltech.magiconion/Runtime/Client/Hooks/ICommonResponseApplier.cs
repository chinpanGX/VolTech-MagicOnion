using Cysharp.Threading.Tasks;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 成功したレスポンスの共通レスポンス項目を反映する。アプリが実装する。
    /// </summary>
    /// <remarks>
    /// TCommon はアプリの共通レスポンス項目の型(<see cref="IApiResponse{TCommon}"/>)。基盤は中身を知らないので、Common ごと渡す。
    /// player_diff → バッジ → 通知の順で反映する。順序はアプリが守る決めごと。
    /// 反映処理でやるのはデータの更新だけにし、ダイアログや画面遷移は書かない。
    /// </remarks>
    /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。</typeparam>
    public interface ICommonResponseApplier<TCommon> where TCommon : class
    {
        /// <summary>
        /// common を反映する。
        /// </summary>
        /// <param name="common">成功したレスポンスの Common。null のときは呼ばれない。</param>
        public UniTask ApplyAsync(TCommon common);
    }
}
