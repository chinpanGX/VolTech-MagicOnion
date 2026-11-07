# Unity クライアントでの使い方

Unity のパッケージ `com.chinpangx.voltech.magiconion` の `ApiCore` で、サーバーの RPC を呼ぶ。作り方の詳細は [実装設計書のクライアント](../designs/implementation/client.md)。

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
    .WithPlayerDiffApplier(new AppPlayerDiffApplier(repository))      // IPlayerDiffApplier<AppCommon>
    .WithBadgeApplier(new AppBadgeApplier(badgeStore))                // IBadgeApplier<AppCommon>
    .WithNotificationApplier(new AppNotificationApplier(queue))       // INotificationApplier<AppCommon>
    .Build();

var gacha = new GachaApi(core, MagicOnionClient.Create<IGachaService>(callInvoker, serializerProvider, filters, clientFactoryProvider));
```

- `ApiCore` はアプリで 1 つにする(順番待ちとセッションを持つため)
- `IDeviceService` のクライアントも、アプリの `[MagicOnionClientGeneration]` の対象に入れる
- `appErrors` は、アプリの業務エラー(`ErrorCode`)と既定の action を組にした `ErrorDefinition` の一覧。予約エラー(`ReservedErrors`)は基盤が入れる

## 共通レスポンス項目の反映

成功したレスポンスが `IApiResponse<AppCommon>`(`AppCommon` は [サーバーでの使い方](server.md#共通レスポンス項目) の型)で `Common` が null でなければ、`ApiCore` が呼び出し側に返す前に、player_diff → バッジ → 通知の順に反映処理へ `Common` を渡す。

- 反映処理はどれも省略できる。渡した反映処理の型引数(`AppCommon`)が食い違うと、`Build` で `InvalidOperationException`
- 反映処理でやるのはデータの更新だけにする。ダイアログや画面遷移は書かない(通知を出すタイミングは画面が決める)
- 反映処理で例外が起きたら、残りは呼ばれず、`CLIENT_ERROR` として共通エラーハンドラーに渡り、呼び出し側には失敗が返る。送り直しはしない
- 変化前の値が要る演出は、呼ぶ前に値を取っておく(返った時点で反映が終わっている)
- サインインなど全件を返すレスポンスの置き換えは、呼び出し側が `await` で受け取った後に書く

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

- 認証が要るか(`[SkipAuth]`)と冪等性の対象か(`[NoSideEffects]` も `[SkipAuth]` も付かない RPC)は、共有インターフェースの属性から基盤が読む。ラッパーには書かない
- ラッパーから呼べるのは、共有インターフェースに直接書いたメソッドだけ。基底インターフェースから受け継いだメソッドの名前を渡すと `ArgumentException` になる
- 引数が複数なら、`request` をタプルにする
- 結果は `ApiResult<T>`。失敗しても例外は投げない。`handledCodes` に入れた業務エラーは、共通エラーハンドラーを通さずに失敗として返る(予約エラーは入れても共通エラーハンドラーが処理する。ただし `ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED` は、いつも呼び出し側に返る。[予約エラー](../spec/errors.md#予約エラー))
- トークンを取り直して再送しても認証が通らなければ、`UNKNOWN_ERROR` として共通エラーハンドラーに渡る。既定ではタイトルに戻る([認証](../spec/auth.md))

## VolTech-gRPC 版から移すとき

- `ErrorCode`・`ReservedErrors` は `VolTech.Server.Abstractions` の型になった(`using VolTech.Server.Abstractions;`)。errors.toml から生成していた業務エラーは、アプリの共有コードに C# の定数で書く
- 生成していたラッパーは、上の形で手で書く
- `DeviceTokenProvider` には `CallInvoker` ではなく `IDeviceService` のクライアントを渡す
- `IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier` は、レスポンス(`IMessage`)ではなく `Common`(アプリの `TCommon`)を受け取る。`IHasPlayerDiff` などでレスポンスから取り出す処理は要らない
- gRPC Monitor、`FakeCallInvoker`、`UnityAccountStore` などの Unity 用の部品は、まだ無い
