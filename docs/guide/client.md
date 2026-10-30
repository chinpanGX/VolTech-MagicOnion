# Unity クライアントでの使い方

Unity のパッケージ `com.chinpangx.voltech.magiconion` の `ApiCore` で、サーバーの RPC を呼ぶ。共通レスポンス項目(player_diff・バッジ・通知)の反映はまだ無い(決まっていない)。作り方の詳細は [実装設計書のクライアント](../design/client.md)。

## 組み立て

```csharp
var channel = ApiChannel.Create("http://localhost:5000");
var callInvoker = ApiChannel.CreateCallInvoker(channel);
var options = new ApiCoreOptions(new AppVersion(AppVersion.Windows, 1));

// MagicOnion のクライアントは、アプリがソースジェネレーターで作ったものを使う(IL2CPP のため)
var deviceClient = MagicOnionClient.Create<IDeviceService>(callInvoker, serializerProvider, filters, clientFactoryProvider);
var tokenProvider = new DeviceTokenProvider(deviceClient, new EncryptedFileAccountStore(path, Application.identifier), options);

var core = ApiCore.CreateBuilder(options, tokenProvider, new ErrorPolicyProvider(appErrors), errorHandler)
    .WithErrorNavigator(navigator)
    .WithLogger(logger)
    .AddActivityHandler(loadingIndicator)
    .Build();

var gacha = new GachaApi(core, MagicOnionClient.Create<IGachaService>(callInvoker, serializerProvider, filters, clientFactoryProvider));
```

- `ApiCore` はアプリで 1 つにする(順番待ちとセッションを持つため)
- `IDeviceService` のクライアントも、アプリの `[MagicOnionClientGeneration]` の対象に入れる
- `appErrors` は、アプリの業務エラー(`ErrorCode`)と既定の action を組にした `ErrorDefinition` の一覧。予約エラー(`ReservedErrors`)は基盤が入れる

## ラッパー

共有インターフェースごとに `ApiServiceBase<TService>` を継承したラッパーを書き、画面からはラッパーを呼ぶ。

```csharp
public sealed class GachaApi : ApiServiceBase<IGachaService>
{
    public GachaApi(ApiCore core, IGachaService client) : base(core, client) { }

    public UniTask<ApiResult<DrawResult>> DrawAsync(int count, IEnumerable<ErrorCode> handledCodes = null)
    {
        return InvokeAsync(nameof(IGachaService.DrawAsync), count, static (c, r) => c.DrawAsync(r), handledCodes);
    }
}
```

- 認証が要るか(`[SkipAuth]`)と冪等性の対象か(`[NoSideEffects]`)は、共有インターフェースの属性から基盤が読む。ラッパーには書かない
- 引数が複数なら、`request` をタプルにする
- 結果は `ApiResult<T>`。失敗しても例外は投げない。`handledCodes` に入れた業務エラーは、共通エラーハンドラーを通さずに失敗として返る(予約エラーは入れても共通エラーハンドラーが処理する。ただし `ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED` は、いつも呼び出し側に返る。[予約エラー](../spec/errors.md#予約エラー))
- トークンを取り直して再送しても認証が通らなければ、`UNKNOWN_ERROR` として共通エラーハンドラーに渡る。既定ではタイトルに戻る([認証](../spec/auth.md))

## VolTech-gRPC 版から移すとき

- `ErrorCode`・`ReservedErrors` は `VolTech.Server.Abstractions` の型になった(`using VolTech.Server.Abstractions;`)。errors.toml から生成していた業務エラーは、アプリの共有コードに C# の定数で書く
- 生成していたラッパーは、上の形で手で書く
- `DeviceTokenProvider` には `CallInvoker` ではなく `IDeviceService` のクライアントを渡す
- `WithPlayerDiffApplier`・`WithBadgeApplier`・`WithNotificationApplier`、gRPC Monitor、`FakeCallInvoker`、`UnityAccountStore` などの Unity 用の部品は、まだ無い
