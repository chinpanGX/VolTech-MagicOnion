# クライアント(Unity)

VolTech-gRPC のクライアント(`com.chinpangx.voltech.grpc` v0.6.0)から作る。通信の骨格は流用できる。アプリの呼び方は MagicOnion の公式に合わせ、共有インターフェースを直接呼ぶ([設計書 1](../common-response-and-client-filter.md))。

| 対象 | 扱い | 理由 |
| --- | --- | --- |
| `ApiChannel`(YetAnotherHttpHandler + `GrpcChannel`) | 流用 | `MagicOnionClient.Create<T>(callInvoker)` に同じ CallInvoker を渡せる |
| `ApiCore` の制御(順番待ち、再認証、セッション世代、進行中通知、エラーハンドラー) | 流用 | 通信手段に依存しない |
| `ErrorConverter`、`ErrorPolicy`、`ReservedErrors`、`ErrorHandling/` | 流用 | サーバーが ErrorInfo を同じ形で返す限り無修正 |
| `EncryptedFileAccountStore` などアカウント保存 | 流用 | `StoredAccount` は VolTech-gRPC 版のまま変えていない(protobuf。MessagePack に移すかは[決めること](../../index.md#決めること)) |
| `ApiHeaders`、`AppVersion`、`ServerClock` | 流用 | ヘッダー名が同じ |
| `ApiCore.InvokeAsync` の `call`(`AsyncUnaryCall<T>`) | 差し替え | `IClientFilter` の `next` に変える。ヘッダーと締め切りは包んだ `CallInvoker` で付ける([設計書 1](../common-response-and-client-filter.md)) |
| `IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier` | 差し替え | `ICommonResponseApplier<TCommon>` 1 つにまとめる([設計書 1](../common-response-and-client-filter.md)) |
| `DeviceTokenProvider` の通信部分 | 差し替え | 生成された `DeviceGrpc` を MagicOnion の `IDeviceService` に替える |
| `ApiServiceBase` と生成ラッパー | 不要 | アプリが共有インターフェースを直接呼ぶ |
| `RpcSettings` | 差し替え | 共有インターフェースの属性から設定を読む |
| Tracing(gRPC Monitor) | 作り直し | [Monitor の設計書](../api-monitor.md) |
| `protoc-gen-voltech-grpc`(ラッパー生成)、buf の設定、`Runtime/Generated/` の RPC 部分 | 不要 | proto を使わなくなる。エラーコードも C# の定数を共有するので生成しない |

`Google.Protobuf` への依存は残す。エラー詳細(`google.rpc.Status`)の解析とアカウント保存で使っているため。

## 移したもの(2026-10-30)

`Runtime/Client/`(asmdef `VolTech.MagicOnion.Client`)に置く。名前空間は VolTech-gRPC 版と同じ `VolTech.Client`(`ApiChannel` は `VolTech.Client.Unity`)のままにし、アプリの `using` を変えずに済むようにした。asmdef が参照するのは `VolTech.Server.Abstractions`・`UniTask`・`Cysharp.Net.Http.YetAnotherHttpHandler` と、NuGetForUnity で入れた DLL(`MagicOnion.Client`・`Grpc.*`・`Google.Protobuf`。自動で参照される)。

| VolTech-gRPC 版 | この基盤 | 変えたところ |
| --- | --- | --- |
| `ApiCore`・`ApiCoreBuilder`・`ApiCoreOptions`・`ApiResult` | 同じ名前 | `ApiCore` は `ApiCallFilter`(`IClientFilter`)から動く。呼び方と `ApiResult<T>` は [設計書 1](../common-response-and-client-filter.md) |
| `ApiServiceBase.RpcSettings`(生成ラッパーが持つ) | `Internal/RpcSettings`(internal) | `CreateClient<T>` のときに `T`(共有インターフェース)の属性から作る。`[SkipAuth]` はインターフェースかメソッド、`[NoSideEffects]` はメソッド。冪等性の対象は、どちらも付かない RPC。サーバーの `RpcSettings` と同じく、共有インターフェースの属性だけを読む([ヘッダーと RPC ごとの設定](../../spec/headers.md#rpc-ごとの設定は属性に置く))。名前は `IGachaService/DrawAsync` の形 |
| `Auth/DeviceTokenProvider` | 同じ名前 | コンストラクターは `CallInvoker` の代わりに `IDeviceService`(アプリが `MagicOnionClient.Create` で作ったもの)を受け取る。`WithOptions` でアプリのバージョンのヘッダーと締め切りを付けて `RegisterAsync(secretKey)`・`IssueTokenAsync(deviceId, secretKey)` を呼ぶ。期限は `IssueTokenResult.ExpiresAt`(UTC の `DateTime`)をそのまま使う |
| `Auth/` のほか(`AccountData`・`IAccountStore`・`EncryptedFileAccountStore`・例外・`AccountStatus`) | 同じ名前 | なし |
| `Runtime/Generated/Messages/VolTech/V1/AccountStore.cs`(`StoredAccount`) | `Generated/AccountStore.cs` | なし(protoc が生成したまま。保存形式を変えない) |
| `Errors/ErrorCode`・`Errors/ReservedErrors`(errors.toml から生成) | `Abstractions` の `ErrorCode`・`ReservedErrors` | 移さず、サーバーと共有する型を使う。予約エラーの既定の action(VolTech-gRPC 版の `ReservedErrors.All`)は `ErrorPolicyProvider` の中に持つ。`ErrorCode.IsReserved`(internal)は `ErrorConverter.IsReserved` にした |
| `Errors/` のほか(`ApiError`・`ErrorAction`・`ErrorDefinition`・`ErrorPolicyProvider`・`IErrorPolicyProvider`) | 同じ名前 | `ErrorCode` が `Abstractions` の型になった |
| `Internal/ApiHeaders` | `Abstractions` の `ApiHeaders` | 移さず、サーバーと共有する定数を使う |
| `Internal/`(`ErrorConverter`・`RpcErrorDetails`・`RpcErrorInfo`・`AesHmacCipher`・`SafeFile`) | 同じ名前 | `ErrorMetadataKeys.Action`・`ApiHeaders.ErrorDetailsTrailer` を使う |
| `Clock/`・`ErrorHandling/`・`Hooks/`(`IApiActivityHandler`・`IApiLogger`・`ITokenProvider`)・`AppVersion` | 同じ名前 | なし |
| `Testing/FakeRpcErrors`・`Testing/InMemoryAccountStore` | 同じ名前 | `FakeRpcErrors` に `ApiFailureResult<T>` を足した |
| `Runtime/Unity/ApiChannel` | `ApiChannel` | Interceptor の一覧を、Monitor が付ける 1 つの `SetInterceptor` に変えた。`CreateCallInvoker` は Interceptor があれば付けて返す。プレイヤーには付ける側が入らないので、チャンネルの CallInvoker をそのまま返す([Monitor の設計書](../api-monitor.md)) |

static フィールドは定数と変えられない値だけにした。例外は Monitor のための `ApiChannel`・`ApiTraceStore`・`ApiMonitorWindow` で、`[NoAutoStaticsCleanup]` を付ける(Unity 7 のアナライザーの UAL0010。[プロジェクト構成](project-structure.md#共有のしかた))。VolTech-gRPC 版の `ApiError` の空の辞書と `DeviceTokenProvider` の `refreshMargin` は、呼ぶたびに作るか定数にした。`Generated/AccountStore.cs` は protoc の生成物のまま static フィールドを持つ。

### 呼び方と共通レスポンス項目の反映

[設計書 1](../common-response-and-client-filter.md)に書く。

### 移していないもの
- `Testing/FakeCallInvoker`・`FakeCall`: アプリ向けには移していない。アプリのテストは共有インターフェースを実装した偽のクライアントで書き、失敗は `FakeRpcErrors.ApiFailureResult<T>` で返す。基盤のテストは `Tests/EditMode/Support/FakeCallInvoker.cs` で本物の `MagicOnionClient` を動かす。MagicOnion は内部の型で通信するので、応答は MessagePack のバイト列で渡す
- `Runtime/Unity/` のうち `ApiChannel` 以外(`UnityAccountStore`・`ServerClockResetter`・`UnityApiLogger`・`UnityAppVersion`): この表に載っておらず、置き場所(`Runtime/Client/` に入れるか、Unity 用の asmdef を分けるか)を決めていない
- `Runtime/Generated/` の RPC 部分(`DeviceGrpc`・`Device`・`AuthOptions`)と `Runtime/Client/Errors/ReservedErrors.cs`(生成物): 不要

## テスト

`Tests/EditMode/`(asmdef `VolTech.MagicOnion.Client.Tests.EditMode`)。Sandbox の `manifest.json` の `testables` にパッケージを入れて、Test Runner の EditMode で動かす。

- VolTech-gRPC 版から移した: `DefaultCommonErrorHandlerTests`・`EncryptedFileAccountStoreTests`・`RpcErrorDetailsTests`・`ServerClockTests`・`DeviceTokenProviderTests`(`FakeCallInvoker` の代わりに、`IDeviceService` を実装した偽のクライアントを使う)
- 足した: `ClientBehaviorTests`。基盤が前提にする MagicOnion のクライアントの動き(`next` の再呼び出し、`Metadata` の使い回し、`WithHeaders`、例外の型など)を、ソースジェネレーターのクライアントで確かめる。MagicOnion の版を上げたときに、前提が崩れていないかを見る
- 足した: `ApiCoreTests`。本物の `MagicOnionClient` と `FakeCallInvoker` で、ヘッダー・締め切り・キャンセル・再認証・再送・順番待ち・共通エラーハンドラー・`ToResult`・`WithHandledCodes` を確かめる
- 足した: `CommonResponseTests`。反映処理に `Common` を渡すこと、`Common` が null なら呼ばないこと、反映の例外を `CLIENT_ERROR` として共通エラーハンドラーに渡して失敗を返すこと(送り直さない)、古いセッションのレスポンスは反映しないこと
- 足した: `ErrorPolicyProviderTests`。`ReservedErrors` の公開フィールドをリフレクションですべて列挙し、どれにも既定の action があり、[予約エラーの表](../../spec/errors.md#予約エラー)と同じであることを確かめる。予約エラーを足して既定の action を入れ忘れると失敗する
- 作り直した: `ApiTraceTests`・`ApiTraceJsonTests`。[Monitor の設計書](../api-monitor.md)の記録と JSON を確かめる

Sandbox(Unity 7000.0.0a12)でコンパイルが通り、EditMode の 85 件がすべて通った(2026-11-07、`unity test <Sandbox のパス> --mode EditMode`)。パッケージのコードに UAL0010 の警告は出なかった。サーバーを相手にした呼び出し(YetAnotherHttpHandler でエラーのトレーラーが届くか、IL2CPP)はまだ確かめていない
