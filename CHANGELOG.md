# Changelog

## [Unreleased]

### Changed

- `VolTech.Server.Abstractions` のソースを Unity パッケージの `Runtime/Abstractions/` に移し、csproj から読み込む形にした
- NuGet の版を `Directory.Packages.props` で一か所に固定する

### Added

- リポジトリの骨組み(ソリューション、CI、`.editorconfig`)
- `VolTech.Server.Abstractions`: `ErrorCode`、`BusinessException`、`ApiHeaders`、`ErrorMetadataKeys`、`RequestIdRules`、`SkipAuthAttribute`、`NoSideEffectsAttribute`
- Unity パッケージ `com.chinpangx.voltech.magiconion` の骨組み(`package.json`、asmdef)と、開発用の Unity 7 プロジェクト `Unity/VolTech.Sandbox`
- `VolTech.Server.Abstractions`: `ReservedErrors`(予約エラーの定数)、`IDeviceService`(デバイス登録とトークンの発行)
- `VolTech.Server`: `AddVoltechServer()`(フィルターの順序を固定する)、`RpcLogFilter`(アクセスログと例外の変換)、`ServerTimeFilter`・`DateChangeFilter`(サーバー時刻とゲーム内の日付)、`AuthFilter`・`DeviceService`・`IPlayerResolver`(認証)、`ITransactions`・`TxContext`・`GameLog`(トランザクション・冪等性・ゲームログ)、`VoltechDatabase.Migrate()`(基盤のテーブル)
