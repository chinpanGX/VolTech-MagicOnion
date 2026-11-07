using MagicOnion.Server;
using MagicOnion.Server.Filters;
using VolTech.Server.Internal;

namespace VolTech.Server.Transaction
{
    /// <summary>
    /// IApiResponse&lt;&gt; のレスポンスが ITransactions.RunAsync を通って返されたかを確かめる。基盤のフィルターの一番内側に置く。
    /// </summary>
    /// <remarks>
    /// Common を詰めるのは RunAsync だけなので、RunAsync の外で作ったレスポンスと、RunAsync を通らないレスポンスは Common が null のまま届く。
    /// クライアントは「変化なし」と読むのでエラーにならず、表示がずれる。それを見つけるために、RunAsync が返したオブジェクトと比べる。
    /// Common は null でも正しい値なので、null かどうかでは判定できない。
    /// 本番でも動く。検出した時点で処理はコミット済みなので、データは守れない。主な目的は開発中に見つけること。
    /// </remarks>
    internal sealed class CommonResponseCheckFilter : IMagicOnionServiceFilter
    {
        public async ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
        {
            await next(context);

            var response = context.GetRawResponse();
            if (response == null || !CommonResponseRegistration.ImplementsApiResponse(response.GetType()))
            {
                return;
            }

            context.Items.TryGetValue(ContextItems.RunResponse, out var returnedByRun);
            if (!ReferenceEquals(response, returnedByRun))
            {
                throw new InvalidOperationException(
                    $"{response.GetType().Name} は IApiResponse<> を実装しているが、ITransactions.RunAsync が返したレスポンスではない。"
                    + "Common が詰まらないので、RunAsync の本体からレスポンスを返す。");
            }
        }
    }
}
