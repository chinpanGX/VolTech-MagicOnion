# Changelog

## [Unreleased]

### 互換性が壊れる変更

- Unity クライアント: `ApiServiceBase<TService>` を消した。アプリは `ApiCore.CreateClient<T>()` で作ったクライアントを直接呼び、`ToResult()` で結果を受け取る。`handledCodes`・`notifyActivity` の引数は `WithHandledCodes`・`WithoutActivity` になった
- Unity クライアント: `ApiCore.CreateBuilder` の引数に `CallInvoker`・シリアライザー・`IMagicOnionClientFactoryProvider` を足した
- Unity クライアント: `IPlayerDiffApplier<TCommon>`・`IBadgeApplier<TCommon>`・`INotificationApplier<TCommon>` を `ICommonResponseApplier<TCommon>` 1 つにまとめた。player_diff → バッジ → 通知の順はアプリが守る
- Unity クライアント: `ApiResult<T>.IsSuccess` を `IsError` に変え、`ErrorCode`・`ErrorMessage` を足した
- `VolTech.Server`: `IApiResponse<>` のレスポンスが `RunAsync` を通らずに返ると `INTERNAL` になる
- サーバーの RPC ごとの設定は、共有インターフェースとそのメソッドの属性(`[SkipAuth]`・`[NoSideEffects]`)だけを読む。実装クラスと実装メソッドに付けた属性は読まなくなった。クライアントは実装クラスを知らないので、実装側の属性まで読むと、サーバーとクライアントで認証の要否や冪等性の対象が食い違うため
- `SkipAuthAttribute` をクラスに付けられなくした(`AttributeUsage` を `Interface | Method` にした)。実装クラスに付けているとコンパイルエラーになる。共有インターフェースに付け直す
- サーバーが属性を読む共有インターフェースを、サービスのクラスの `ServiceBase<T>` の `T` だけにした。実装クラスがほかに実装しているインターフェースの属性と、共有インターフェースが基底インターフェースから受け継いだメソッドの属性は読まなくなった
- クライアントで、`[SkipAuth]` の RPC を冪等性の対象から外した。`x-request-id` を付けず、順番待ちもしない(サーバーは前から対象にしていない)
- `devices`・`last_responses`・`game_logs` の `player_id` を `uuid` から `char(26)`(ULID の文字列)に変えた。既存のマイグレーションを書き換えたので、手元の DB は作り直す

### Fixed

- Windows の IL2CPP のビルドが `Microsoft.Bcl.AsyncInterfaces` の `IL1009` で失敗する問題を直した。原因は DLL の `.meta` の `Exclude Editor: 1`(古い形式)で、Unity 7 がこの DLL をプレイヤーに渡さなかったこと。`Any` 有効・`Editor` 無効の形式にし、導入ガイドに書いた

### Changed

- クライアントの `ApiCore`: 取り直したトークンでも `UNAUTHENTICATED` なら、そのトークンも捨ててから `UNKNOWN_ERROR` にする。共通エラーハンドラーの Retry やほかの RPC が、弾かれたトークンでまた弾かれないようにした
- `VolTech.Server.Abstractions` のソースを Unity パッケージの `Runtime/Abstractions/` に移し、csproj から読み込む形にした
- NuGet の版を `Directory.Packages.props` で一か所に固定する

### Added

- Unity クライアント: `ApiCore.CreateClient<T>()`・`ToResult()`・`WithHandledCodes`・`WithoutActivity`・`ApiException`・`FakeRpcErrors.ApiFailureResult<T>`
- `VolTech.Server`: `CommonResponseCheckFilter`(`FilterOrder.CommonResponseCheck`)
- 共通レスポンス項目(player_diff・バッジ・通知): レスポンスの型が `IApiResponse<TCommon>`(`Abstractions`)を実装し、`Common` に載せて返す。`TCommon` はアプリが決める 1 つの型
- `VolTech.Server`: `ICommonResponseCollector<TCommon>`・`TxContext.GetCollector<TCollector>()`・`AddCommonResponse<TCommon, TCollector>()`。`ITransactions.RunAsync` が本体の成功後、保存の前に `Common` を詰める
- Unity クライアント: `IPlayerDiffApplier<TCommon>`・`IBadgeApplier<TCommon>`・`INotificationApplier<TCommon>` と `ApiCoreBuilder.WithPlayerDiffApplier`・`WithBadgeApplier`・`WithNotificationApplier`。成功したレスポンスの `Common` を player_diff → バッジ → 通知の順に渡し、反映の例外は `CLIENT_ERROR` にする

- リポジトリの骨組み(ソリューション、CI、`.editorconfig`)
- `VolTech.Server.Abstractions`: `ErrorCode`、`BusinessException`、`ApiHeaders`、`ErrorMetadataKeys`、`RequestIdRules`、`SkipAuthAttribute`、`NoSideEffectsAttribute`
- Unity パッケージ `com.chinpangx.voltech.magiconion` の骨組み(`package.json`、asmdef)と、開発用の Unity 7 プロジェクト `Unity/VolTech.Sandbox`
- `VolTech.Server.Abstractions`: `ReservedErrors`(予約エラーの定数)、`IDeviceService`(デバイス登録とトークンの発行)
- Unity クライアント(`Runtime/Client/`): VolTech-gRPC のクライアントから `ApiCore`・`ApiCoreBuilder`・`ApiServiceBase<TService>`・`DeviceTokenProvider`・エラー処理・アカウント保存・`ApiChannel` を MagicOnion 版にして移した。共通レスポンス項目の反映(`IPlayerDiffApplier` など)と Tracing は移していない
- `tests/VolTech.Server.Tests`: フォルダーの間の依存の向きのテスト(NetArchTest)
- `VolTech.Server`: `AddVoltechServer()`(フィルターの順序を固定する)、`RpcLogFilter`(アクセスログと例外の変換)、`ServerTimeFilter`・`DateChangeFilter`(サーバー時刻とゲーム内の日付)、`AuthFilter`・`DeviceService`・`IPlayerResolver`(認証)、`ITransactions`・`TxContext`・`GameLog`(トランザクション・冪等性・ゲームログ)、`VoltechDatabase.Migrate()`(基盤のテーブル)
