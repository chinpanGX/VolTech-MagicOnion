# VolTech MagicOnion

MagicOnion でゲームのバックエンドを作るための基盤。C# のサーバー側ライブラリと、Unity のクライアント(UPM パッケージ)からなる。開発中(まだ使えない)。

proto と Rust サーバーで同じ役割を果たす [VolTech-gRPC](https://github.com/chinpanGX/VolTech-gRPC) とは別のリポジトリで、通信の取り決め(エラーの形式・ヘッダー名)は同じにそろえる。マスターデータの基盤(MasterDataTools)も別のリポジトリで、このリポジトリからは参照しない。

| 場所 | 中身 |
|---|---|
| `src/VolTech.Server.Abstractions/` | サーバーとクライアントで共有する型(エラーコード・業務エラー・ヘッダー名・属性)。netstandard2.1 |
| `tests/` | テスト |
| `docs/` | 設計と利用ガイド |

サーバー側の `VolTech.Server`(Status・Auth・Transaction・Logging・Hosting をフォルダーで分ける)と、Unity クライアントは順に足していく。計画は [docs/index.md](docs/index.md)。

## 開発

.NET 10 SDK が要る。

```
dotnet build VolTech-MagicOnion.slnx
dotnet test VolTech-MagicOnion.slnx
```

## ライセンス

MIT。[LICENSE](LICENSE)。
