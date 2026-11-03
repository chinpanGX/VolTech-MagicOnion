using Cysharp.Threading.Tasks;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// 成功したレスポンスの通知を、待ち行列に積む。アプリが実装する。
    /// </summary>
    /// <remarks>
    /// TCommon はアプリの共通レスポンス項目の型(<see cref="IApiResponse{TCommon}"/>)。基盤は中身を知らないので、Common ごと渡す。
    /// ここでは積むだけにし、通知を出すタイミングは画面が決める(反映処理の中で出すと、ガチャの演出の途中に割り込む)。
    /// </remarks>
    /// <typeparam name="TCommon">アプリの共通レスポンス項目の型。</typeparam>
    public interface INotificationApplier<TCommon> where TCommon : class
    {
        /// <summary>
        /// common に通知があれば積む。無ければ何もしない。
        /// </summary>
        /// <param name="common">成功したレスポンスの Common。null のときは呼ばれない。</param>
        public UniTask ApplyAsync(TCommon common);
    }
}
