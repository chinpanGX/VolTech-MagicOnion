# Changelog

## [Unreleased]

### 互換性が壊れる変更

- サーバーの RPC ごとの設定は、共有インターフェースとそのメソッドの属性(`[SkipAuth]`・`[NoSideEffects]`)だけを読む。実装クラスと実装メソッドに付けた属性は読まなくなった。クライアントは実装クラスを知らないので、実装側の属性まで読むと、サーバーとクライアントで認証の要否や冪等性の対象が食い違うため
- `SkipAuthAttribute` をクラスに付けられなくした(`AttributeUsage` を `Interface | Method` にした)。実装クラスに付けているとコンパイルエラーになる。共有インターフェースに付け直す

### Changed

- `VolTech.Server.Abstractions` のソースを Unity パッケージの `Runtime/Abstractions/` に移し、csproj から読み込む形にした
- NuGet の版を `Directory.Packages.props` で一か所に固定する

### Added

- リポジトリの骨組み(ソリューション、CI、`.editorconfig`)
- `VolTech.Server.Abstractions`: `ErrorCode`、`BusinessException`、`ApiHeaders`、`ErrorMetadataKeys`、`RequestIdRules`、`SkipAuthAttribute`、`NoSideEffectsAttribute`
- Unity パッケージ `com.chinpangx.voltech.magiconion` の骨組み(`package.json`、asmdef)と、開発用の Unity 7 プロジェクト `Unity/VolTech.Sandbox`
- `VolTech.Server.Abstractions`: `ReservedErrors`(予約エラーの定数)、`IDeviceService`(デバイス登録とトークンの発行)
- Unity クライアント(`Runtime/Client/`): VolTech-gRPC のクライアントから `ApiCore`・`ApiCoreBuilder`・`ApiServiceBase<TService>`・`DeviceTokenProvider`・エラー処理・アカウント保存・`ApiChannel` を MagicOnion 版にして移した。共通レスポンス項目の反映(`IPlayerDiffApplier` など)と Tracing は移していない
- `tests/VolTech.Server.Tests`: フォルダーの間の依存の向きのテスト(NetArchTest)
- `VolTech.Server`: `AddVoltechServer()`(フィルターの順序を固定する)、`RpcLogFilter`(アクセスログと例外の変換)、`ServerTimeFilter`・`DateChangeFilter`(サーバー時刻とゲーム内の日付)、`AuthFilter`・`DeviceService`・`IPlayerResolver`(認証)、`ITransactions`・`TxContext`・`GameLog`(トランザクション・冪等性・ゲームログ)、`VoltechDatabase.Migrate()`(基盤のテーブル)
