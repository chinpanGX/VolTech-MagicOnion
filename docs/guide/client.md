# Unity クライアントでの使い方

Unity のパッケージ `com.chinpangx.voltech.magiconion` の `ApiCore` で、サーバーの RPC を呼ぶ。作り方の詳細は [実装設計書のクライアント](../designs/implementation/client.md)。

## 組み立て

```csharp
var channel = ApiChannel.Create("http://localhost:5000");
var callInvoker = ApiChannel.CreateCallInvoker(channel);
var options = new ApiCoreOptions(new AppVersion(AppVersion.Windows, 1));

// MagicOnion のクライアントは、アプリがソースジェネレーターで作ったものを使う(IL2CPP のため)
var deviceClient = MagicOnionClient.Create<IDeviceService>(
    callInvoker, serializerProvider, Array.Empty<IClientFilter>(), clientFactoryProvider);
var tokenProvider = new DeviceTokenProvider(deviceClient, new EncryptedFileAccountStore(path, Application.identifier), options);

var core = ApiCore.CreateBuilder(
        callInvoker, serializerProvider, clientFactoryProvider, options,
        tokenProvider, new ErrorPolicyProvider(appErrors), errorHandler)
    .WithErrorNavigator(navigator)
    .WithLogger(logger)
    .AddActivityHandler(loadingIndicator)
    .WithCommonResponseApplier(new AppCommonResponseApplier(repository, badgeStore, queue))   // ICommonResponseApplier<AppCommon>
    .Build();

var gachaClient = core.CreateClient<IGachaService>();
```

- `ApiCore` はアプリで 1 つにする(順番待ちとセッションを持つため)
- `[MagicOnionClientGeneration]` には、アプリの共有インターフェースのアセンブリの型と、`IDeviceService` を書く。書いた型のアセンブリ全体が生成の対象になる。書き忘れはコンパイルでは分からず、クライアントを作るときに `NotSupportedException` になる
- `appErrors` は、アプリの業務エラー(`ErrorCode`)と既定の action を組にした `ErrorDefinition` の一覧。予約エラー(`ReservedErrors`)は基盤が入れる

## 共通レスポンス項目の反映

成功したレスポンスが `IApiResponse<AppCommon>`(`AppCommon` は [サーバーでの使い方](server.md#共通レスポンス項目) の型)で `Common` が null でなければ、基盤が呼び出し側に返す前に、反映処理へ `Common` を渡す。

- 反映処理は省略できる
- `ICommonResponseApplier<AppCommon>` の中で、player_diff → バッジ → 通知の順に反映する。順序はアプリが守る
- 反映処理でやるのはデータの更新だけにする。ダイアログや画面遷移は書かない(通知を出すタイミングは画面が決める)
- 反映処理で例外が起きたら、`CLIENT_ERROR` として共通エラーハンドラーに渡り、呼び出し側には失敗が返る。送り直しはしない
- 変化前の値が要る演出は、呼ぶ前に値を取っておく(返った時点で反映が終わっている)
- サインインなど全件を返すレスポンスの置き換えは、呼び出し側が `await` で受け取った後に書く

## 呼び方

共有インターフェースを MagicOnion の公式の書き方で呼び、最後に `ToResult()` を付ける。`try/catch` は書かない。

```csharp
// 普通の呼び方。エラーは共通エラーハンドラーが処理する
var result = await gachaClient.DrawAsync(request).ToResult();
if (result.IsError)
{
    return;
}

Show(result.Value);

// 自分で処理するコードを指定する
var result = await gachaClient
    .WithHandledCodes(GachaErrors.NotEnoughGems)
    .DrawAsync(request)
    .ToResult();
if (result.IsError && result.ErrorCode == GachaErrors.NotEnoughGems)
{
    ShowShop();
}

// 進行中の表示を出さずに呼ぶ。エラーは共通エラーハンドラーが処理する
var result = await gachaClient.WithoutActivity().DrawAsync(request).ToResult();
```

- 結果は `ApiResult<T>`。`IsError`・`ErrorCode`・`ErrorMessage`・`Error`・`Value` を持つ。`ErrorMessage` はログ用で、ユーザーには見せない。失敗のときに `Value` を読むと例外になる
- `ToResult()` を付け忘れると、共通エラーハンドラーの処理の後で `ApiException` が呼び出し側に届く。コンパイルでは分からない
- `WithHandledCodes` で指定した業務エラーは、共通エラーハンドラーを通さずに失敗として返る(予約エラーは指定しても共通エラーハンドラーが処理する。ただし `ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED` は、いつも呼び出し側に返る。[予約エラー](../spec/errors.md#予約エラー))
- `WithHandledCodes` は RPC を呼ぶ前につなぐ。指定したクライアントを変数に持って何度呼んでも効く
- `WithHandledCodes` と `WithoutActivity` を続けてつながない。後の指定だけが残る。両方が要るときは `WithoutActivity(GachaErrors.NotEnoughGems)` と書く
- `WithHeaders`・`WithOptions`・`WithDeadline`・`WithCancellationToken` は使わない。基盤の指定が消えるか、基盤が置き換えるので効かない
- 認証が要るか(`[SkipAuth]`)と冪等性の対象か(`[NoSideEffects]` も `[SkipAuth]` も付かない RPC)は、共有インターフェースの属性から基盤が読む
- 呼べるのは、共有インターフェースに直接書いたメソッドだけ。基底インターフェースから受け継いだメソッドは、コンパイルエラーになる
- レスポンスの無い `UnaryResult` の RPC には対応しない。レスポンスの型を返すように書く
- トークンを取り直して再送しても認証が通らなければ、`UNKNOWN_ERROR` として共通エラーハンドラーに渡る。既定ではタイトルに戻る([認証](../spec/auth.md))

アプリのテストでは、`IGachaService` を実装した偽物をそのまま使える。失敗を返すときは、`FakeRpcErrors.ApiFailureResult<T>` で作った `UnaryResult<T>` を返す。偽物のメソッドから同期で例外を投げると、`ToResult` に届かない。

## Monitor

通信のリクエストとレスポンスを 1 件ずつ見る Editor のウィンドウ。メニューの **Window > VolTech MagicOnion > Monitor** で開く。アプリは何も書かなくても記録される。ウィンドウを閉じていても記録は続く。

- 記録するのは `ApiChannel.CreateCallInvoker` で作った `CallInvoker` の通信。トークンの発行も入る。`GrpcChannel` をそのままクライアントに渡した通信は記録されない
- 記録は Editor のメモリーにだけあり、プレイヤーには入らない。Play を止めても見返せて、次の Play で消える(Clear on Play)
- `authorization`、`secretKey`、`AccessToken` はそのまま見える。Editor から本番のサーバーにつなぐと、本番の値も見える。画面の共有とスクリーンショット、Copy に気を付ける
- Body は受信したバイト列そのものではなく、クライアントの型で読み直した値を JSON にしたもの。古い型は知らない項目を出さない。読めない通信は Body が空になる。表に無い型があると、Body 全体が項目名の無い配列になる
- 複数の引数の RPC の Body は配列になる。引数の名前は残らない

## VolTech-gRPC 版から移すとき

- `ErrorCode`・`ReservedErrors` は `VolTech.Server.Abstractions` の型になった(`using VolTech.Server.Abstractions;`)。errors.toml から生成していた業務エラーは、アプリの共有コードに C# の定数で書く
- 生成していたラッパーは無くなった。共有インターフェースを上の形で直接呼ぶ
- `DeviceTokenProvider` には `CallInvoker` ではなく `IDeviceService` のクライアントを渡す
- 3 つの反映処理は `ICommonResponseApplier<TCommon>` 1 つになった。レスポンス(`IMessage`)ではなく `Common`(アプリの `TCommon`)を受け取る。`IHasPlayerDiff` などでレスポンスから取り出す処理は要らない
- gRPC Monitor は [Monitor](#monitor) になった。実機の表示はまだ無い
- `FakeCallInvoker`、`UnityAccountStore` などの Unity 用の部品は、まだ無い
