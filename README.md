# VolTech MagicOnion

MagicOnion でゲームのバックエンドを作るための基盤。C# のサーバー側ライブラリと、Unity のクライアント(UPM パッケージ)からなる。開発中(まだ使えない)。

proto と Rust サーバーで同じ役割を果たす [VolTech-gRPC](https://github.com/chinpanGX/VolTech-gRPC) とは別のリポジトリで、通信の取り決め(エラーの形式・ヘッダー名)は同じにそろえる。マスターデータの基盤(MasterDataTools)も別のリポジトリで、このリポジトリからは参照しない。

| 場所 | 中身 |
|---|---|
| `src/VolTech.Server.Abstractions/` | サーバーとクライアントで共有する型(エラーコード・業務エラー・ヘッダー名・属性・`IDeviceService`)の csproj。netstandard2.1。ソースは Unity パッケージの `Runtime/Abstractions/` を読み込む |
| `src/VolTech.Server/` | サーバー側の基盤。認証・ゲーム内の日付・トランザクションと冪等性・ゲームログ・アクセスログ。net10 |
| `tests/` | テスト |
| `Unity/com.chinpangx.voltech.magiconion/` | Unity クライアントの UPM パッケージ(共有する型と `ApiCore`。共通レスポンス項目の反映はまだ無い) |
| `Unity/VolTech.Sandbox/` | パッケージを確かめるための開発用 Unity プロジェクト。Unity 7(7000.0.0a12)で開く |
| `docs/` | 仕様書・実装設計書・利用ガイド |

サーバー側はできている。Unity クライアントは `ApiCore` まで移した。計画と進み具合は [docs/index.md](docs/index.md)。

## ドキュメント

- 使う人: [利用ガイド](docs/guide/index.md)。アプリへの導入、[使う版](docs/guide/getting-started.md#使う版)、[サーバーでの使い方](docs/guide/server.md)、[Unity クライアントでの使い方](docs/guide/client.md)
- 作る人: [仕様書](docs/spec/index.md)と[実装設計書](docs/design/index.md)

## 開発

.NET 10 SDK が要る。`VolTech.Server.Tests` の一部は PostgreSQL をコンテナで動かす(Testcontainers)ので、Docker が要る。

```
dotnet build VolTech-MagicOnion.slnx
dotnet test VolTech-MagicOnion.slnx
```

使うライブラリの版は [利用ガイドの使う版](docs/guide/getting-started.md#使う版)。Sandbox もこの版で入れている。

## ライセンス

MIT。[LICENSE](LICENSE)。
