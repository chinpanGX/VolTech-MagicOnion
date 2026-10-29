# クライアント(Unity)

VolTech-gRPC のクライアント(`com.chinpangx.voltech.grpc` v0.6.0)から作る。通信の骨格は流用でき、変わるのは `call` の型、レスポンス型の扱い、`DeviceTokenProvider` の通信部分の 3 点。`ApiCoreBuilder` の公開 API は残し、中身だけを差し替えれば、アプリ側のコードは書き直さずに済む(共通レスポンス項目の部品は除く。下の「移していないもの」)。

| 対象 | 扱い | 理由 |
| --- | --- | --- |
| `ApiChannel`(YetAnotherHttpHandler + `GrpcChannel`) | 流用 | `MagicOnionClient.Create<T>(callInvoker)` に同じ CallInvoker を渡せる |
| `ApiCore` の制御(順番待ち、再認証、セッション世代、進行中通知、エラーハンドラー) | 流用 | 通信手段に依存しない |
| `ErrorConverter`、`ErrorPolicy`、`ReservedErrors`、`ErrorHandling/` | 流用 | サーバーが ErrorInfo を同じ形で返す限り無修正 |
| `EncryptedFileAccountStore` などアカウント保存 | 流用 | `StoredAccount` は暫定で protobuf のまま残す(MessagePack に移すかは[決めること](../index.md#決めること)) |
| `ApiHeaders`、`AppVersion`、`ServerClock` | 流用 | ヘッダー名が同じ |
| `ApiCore.InvokeAsync` の `call`(`AsyncUnaryCall<T>`) | 差し替え | `UnaryResult<T>` に変える。`WithOptions(CallOptions)` でヘッダーと締め切りを渡す |
| `IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier` | 差し替え | `IMessage` を `IApiResponse` に変える |
| `DeviceTokenProvider` の通信部分 | 差し替え | 生成された `DeviceGrpc` を MagicOnion の `IDeviceService` に替える |
| `ApiServiceBase` と生成ラッパー、`RpcSettings` | 差し替え | 共有インターフェースの属性から設定を読む |
| Tracing(gRPC Monitor) | 要確認 | protobuf メッセージの記録が前提だと思われる(未読) |
| `protoc-gen-voltech-grpc`(ラッパー生成)、buf の設定、`Runtime/Generated/` の RPC 部分 | 不要 | proto を使わなくなる。エラーコードも C# の定数を共有するので生成しない |

`Google.Protobuf` への依存は残す。エラー詳細(`google.rpc.Status`)の解析とアカウント保存で使っているため。

## 移したもの(2026-10-30)

`Runtime/Client/`(asmdef `VolTech.MagicOnion.Client`)に置く。名前空間は VolTech-gRPC 版と同じ `VolTech.Client`(`ApiChannel` は `VolTech.Client.Unity`)のままにし、アプリの `using` を変えずに済むようにした。asmdef が参照するのは `VolTech.Server.Abstractions`・`UniTask`・`Cysharp.Net.Http.YetAnotherHttpHandler` と、NuGetForUnity で入れた DLL(`MagicOnion.Client`・`Grpc.*`・`Google.Protobuf`。自動で参照される)。

| VolTech-gRPC 版 | この基盤 | 変えたところ |
| --- | --- | --- |
| `ApiCore`・`ApiCoreBuilder`・`ApiCoreOptions`・`ApiResult` | 同じ名前 | `call` を `Func<TClient, TRequest, UnaryResult<TResponse>>` にした。`ApiCore` が `client.WithOptions(new CallOptions(headers, deadline, cancellationToken))` を作ってから `call` に渡す。`TClient` は `IService<TClient>`。共通レスポンス項目の反映を外した(下) |
| `ApiServiceBase`(生成ラッパーの基底) | `ApiServiceBase<TService>` | 下の「ラッパーの書き方」 |
| `ApiServiceBase.RpcSettings`(生成ラッパーが持つ) | `Internal/RpcSettings`(internal) | `TService`(共有インターフェース)の属性から作る。`[SkipAuth]` はインターフェースかメソッド、`[NoSideEffects]` はメソッド。サーバーの `RpcSettings` と同じく、共有インターフェースの属性だけを読む([ヘッダーと RPC ごとの設定](../spec/headers.md#rpc-ごとの設定は属性に置く))。名前は `IGachaService/DrawAsync` の形 |
| `Auth/DeviceTokenProvider` | 同じ名前 | コンストラクターは `CallInvoker` の代わりに `IDeviceService`(アプリが `MagicOnionClient.Create` で作ったもの)を受け取る。`WithOptions` でアプリのバージョンのヘッダーと締め切りを付けて `RegisterAsync(secretKey)`・`IssueTokenAsync(deviceId, secretKey)` を呼ぶ。期限は `IssueTokenResult.ExpiresAt`(UTC の `DateTime`)をそのまま使う |
| `Auth/` のほか(`AccountData`・`IAccountStore`・`EncryptedFileAccountStore`・例外・`AccountStatus`) | 同じ名前 | なし |
| `Runtime/Generated/Messages/VolTech/V1/AccountStore.cs`(`StoredAccount`) | `Generated/AccountStore.cs` | なし(protoc が生成したまま。保存形式を変えない)。暫定で protobuf のまま残す |
| `Errors/ErrorCode`・`Errors/ReservedErrors`(errors.toml から生成) | `Abstractions` の `ErrorCode`・`ReservedErrors` | 移さず、サーバーと共有する型を使う。予約エラーの既定の action(VolTech-gRPC 版の `ReservedErrors.All`)は `ErrorPolicyProvider` の中に持つ。`ErrorCode.IsReserved`(internal)は `ErrorConverter.IsReserved` にした |
| `Errors/` のほか(`ApiError`・`ErrorAction`・`ErrorDefinition`・`ErrorPolicyProvider`・`IErrorPolicyProvider`) | 同じ名前 | `ErrorCode` が `Abstractions` の型になった |
| `Internal/ApiHeaders` | `Abstractions` の `ApiHeaders` | 移さず、サーバーと共有する定数を使う |
| `Internal/`(`ErrorConverter`・`RpcErrorDetails`・`RpcErrorInfo`・`AesHmacCipher`・`SafeFile`) | 同じ名前 | `ErrorMetadataKeys.Action`・`ApiHeaders.ErrorDetailsTrailer` を使う |
| `Clock/`・`ErrorHandling/`・`Hooks/`(`IApiActivityHandler`・`IApiLogger`・`ITokenProvider`)・`AppVersion` | 同じ名前 | なし |
| `Testing/FakeRpcErrors`・`Testing/InMemoryAccountStore` | 同じ名前 | なし |
| `Runtime/Unity/ApiChannel` | `ApiChannel` | `AddInterceptor` と、Interceptor を付ける CallInvoker を外した(Tracing のためだけの仕組みで、static の可変なフィールドを持つため)。`CreateCallInvoker` はチャンネルの CallInvoker をそのまま返す |

static フィールドは定数と変えられない値だけにした(Unity 7 のアナライザーの UAL0010。[プロジェクト構成](project-structure.md#共有のしかた))。VolTech-gRPC 版の `ApiError` の空の辞書と `DeviceTokenProvider` の `refreshMargin` は、呼ぶたびに作るか定数にした。`Generated/AccountStore.cs` は protoc の生成物のまま static フィールドを持つ。

### ラッパーの書き方

VolTech-gRPC 版はラッパーを proto から生成していた。MagicOnion では生成しないので、アプリが共有インターフェースごとに書く。

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

- `InvokeAsync(methodName, request, call, handledCodes = null, notifyActivity = true)`。`methodName` で RPC ごとの設定を引く。`TService` に無い名前なら `ArgumentException`
- 引数が複数の RPC は、`request` をタプルにして `call` の中で展開する
- RPC ごとの設定は、コンストラクターで `TService` のメソッドを読んで表にする(リフレクション)。MagicOnion はメソッドのオーバーロードを許さないので、名前で一意に決まる
- リフレクションにした理由: 読むのはラッパーを作るときの 1 回だけで、呼び出しのたびには読まない。ソースジェネレーターにすると、`Abstractions` の属性を読む生成器を基盤で作って Unity とサーバーの両方に入れることになり、今の規模では手間に見合わない。サーバーも同じ属性をリフレクションで読むので、読み方を揃えやすい。IL2CPP(AOT)でも属性を読めるかは確かめていない([確かめること](../index.md#確かめること))。読めなかったら、ソースジェネレーターか、ラッパーに設定を書く形に変える
- 対応するのは `UnaryResult<T>` を返すメソッドだけ。レスポンスの無い `UnaryResult` には対応していない

### 移していないもの

- `IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier` と、`ApiCoreBuilder.WithPlayerDiffApplier`・`WithBadgeApplier`・`WithNotificationApplier`: `IApiResponse` が決まっていない([決めること](../index.md#決めること))ため。`ApiCore` の、成功したレスポンスの共通レスポンス項目を player_diff → バッジ → 通知の順に反映する処理(`ApplyCommonFieldsAsync`。反映の例外を `CLIENT_ERROR` にしてエラーハンドラーに渡す)も外した。今の `ApiCore` は、成功したらレスポンスヘッダー(`x-server-time`・`x-game-date`)だけを反映して返す
- Tracing(`Runtime/Tracing/`、`Editor/` の gRPC Monitor): 要確認のため
- `Testing/FakeCallInvoker`・`FakeCall`: MagicOnion のクライアントは CallInvoker に MagicOnion の内部の型でリクエストを渡すので、RPC ごとに型付きの処理を登録する形のままでは使えない。テストは共有インターフェースを実装した偽のクライアントで書いた(`Tests/EditMode/`)。アプリ向けの偽のクライアントの形は決めていない
- `Runtime/Unity/` のうち `ApiChannel` 以外(`UnityAccountStore`・`ServerClockResetter`・`UnityApiLogger`・`UnityAppVersion`): この表に載っておらず、置き場所(`Runtime/Client/` に入れるか、Unity 用の asmdef を分けるか)を決めていない
- `Runtime/Generated/` の RPC 部分(`DeviceGrpc`・`Device`・`AuthOptions`)と `Runtime/Client/Errors/ReservedErrors.cs`(生成物): 不要

## テスト

`Tests/EditMode/`(asmdef `VolTech.MagicOnion.Client.Tests.EditMode`)。Sandbox の `manifest.json` の `testables` にパッケージを入れて、Test Runner の EditMode で動かす。

- VolTech-gRPC 版から移した: `DefaultCommonErrorHandlerTests`・`EncryptedFileAccountStoreTests`・`RpcErrorDetailsTests`・`ServerClockTests`・`DeviceTokenProviderTests`(`FakeCallInvoker` の代わりに、`IDeviceService` を実装した偽のクライアントを使う)
- 足した: `ApiCoreTests`。属性から RPC ごとの設定を読むこと、`authorization`・`x-request-id`・締め切りの付け方、`x-server-time`・`x-game-date` の反映、`UNAUTHENTICATED` での 1 回の再送(2 回続けて `UNAUTHENTICATED` なら、それ以上送らずにエラーハンドラーに渡す)、セッションのキャンセルトークンを `CallOptions` に渡すこと、エラーハンドラーが Retry を返したら同じ RequestID で送り直すこと、`ResetSession` でトークンとゲーム内の日付を捨てること、エラーの変換と `handledCodes`。偽のクライアントはレスポンスヘッダーを読めるように、`IResponseContext<T>` を包んだ `UnaryResult<T>` を返す
- 足した: `ErrorPolicyProviderTests`。`ReservedErrors` の公開フィールドをリフレクションですべて列挙し、どれにも既定の action があり、[予約エラーの表](../spec/errors.md#予約エラー)と同じであることを確かめる。予約エラーを足して既定の action を入れ忘れると失敗する
- 移していない: `ApiTraceTests`(Tracing)

Sandbox(Unity 7000.0.0a12)でコンパイルが通り、EditMode の 38 件がすべて通った(2026-10-30、`unity test VolTech.Sandbox --mode EditMode`)。パッケージのコードに UAL0010 の警告は出なかった(`Generated/AccountStore.cs` を含む)。サーバーを相手にした呼び出し(YetAnotherHttpHandler でエラーのトレーラーが届くか、IL2CPP)はまだ確かめていない
