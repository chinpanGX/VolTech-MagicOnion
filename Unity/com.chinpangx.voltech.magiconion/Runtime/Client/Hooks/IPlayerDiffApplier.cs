using Cysharp.Threading.Tasks;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 成功したレスポンスの player_diff を、アプリのデータ(Repository)に反映する。アプリが実装する。
    /// </summary>
    /// <remarks>
    /// TCommon はアプリの共通レスポンス項目の型(<see cref="IApiResponse{TCommon}"/>)。基盤は中身を知らないので、Common ごと渡す。
    /// 反映処理でやるのはデータの更新だけにし、ダイアログや画面遷移は書かない。
    /// </remarks>
    /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。</typeparam>
    public interface IPlayerDiffApplier<TCommon> where TCommon : class
    {
        /// <summary>
        /// common に player_diff があれば反映する。無ければ何もしない。
        /// </summary>
        /// <param name="common">成功したレスポンスの Common。null のときは呼ばれない。</param>
        public UniTask ApplyAsync(TCommon common);
    }
}
