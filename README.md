# VolTech MagicOnion

MagicOnion でゲームのバックエンドを作るための基盤。C# のサーバー側ライブラリと、Unity のクライアント(UPM パッケージ)からなる。開発中(まだ使えない)。

proto と Rust サーバーで同じ役割を果たす [VolTech-gRPC](https://github.com/chinpanGX/VolTech-gRPC) とは別のリポジトリで、通信の取り決め(エラーの形式・ヘッダー名)は同じにそろえる。マスターデータの基盤(MasterDataTools)も別のリポジトリで、このリポジトリからは参照しない。

| 場所 | 中身 |
|---|---|
| `src/VolTech.Server.Abstractions/` | サーバーとクライアントで共有する型(エラーコード・業務エラー・ヘッダー名・属性・`IDeviceService`)の csproj。netstandard2.1。ソースは Unity パッケージの `Runtime/Abstractions/` を読み込む |
| `src/VolTech.Server/` | サーバー側の基盤。認証・ゲーム内の日付・トランザクションと冪等性・ゲームログ・アクセスログ。net10 |
| `tests/` | テスト |
| `Unity/com.chinpangx.voltech.magiconion/` | Unity クライアントの UPM パッケージ(共有する型だけ。クライアントは未実装) |
| `Unity/VolTech.Sandbox/` | パッケージを確かめるための開発用 Unity プロジェクト。Unity 7(7000.0.0a12)で開く |
| `docs/` | 設計と利用ガイド |

Unity クライアントはこれから足す。計画は [docs/index.md](docs/index.md)、アプリのサーバーからの使い方は [設計書](docs/design.md#アプリからの使い方)。

## 開発

.NET 10 SDK が要る。`VolTech.Server.Tests` の一部は PostgreSQL をコンテナで動かす(Testcontainers)ので、Docker が要る。

```
dotnet build VolTech-MagicOnion.slnx
dotnet test VolTech-MagicOnion.slnx
```

## 使う版

アプリのサーバーとクライアントも、基盤と同じ版を使う。

| ライブラリ | 版 | Unity での入れ方 |
|---|---|---|
| Unity | 7000.0.0a12 | |
| MagicOnion(.Client / .Server) | 7.11.0 | NuGetForUnity で `MagicOnion.Client`、UPM で `MagicOnion.Client.Unity` |
| MessagePack | 3.1.12 | NuGetForUnity で `MessagePack`、UPM で `MessagePack.Unity` |
| YetAnotherHttpHandler | 1.11.7 | UPM |
| Grpc.Net.Client / Grpc.Core.Api / Grpc.Net.Common | 2.84.0 | NuGetForUnity(MagicOnion.Client の依存。最低の版で入るので、最新を明示して入れる) |
| NuGetForUnity | 4.5.0 | UPM |
| Npgsql | 10.0.3 | (サーバーだけ) |
| dbup-postgresql | 7.0.1 | (サーバーだけ) |
| Ulid | 1.4.1 | (サーバーだけ) |
| Grpc.StatusProto | 2.84.0 | (サーバーだけ) |

UPM の git URL は `Unity/VolTech.Sandbox/Packages/manifest.json`、NuGet のパッケージは `Unity/VolTech.Sandbox/Assets/packages.config` にある。YetAnotherHttpHandler が要る System.IO.Pipelines と System.Runtime.CompilerServices.Unsafe は、Unity 7 が持っているので NuGet から入れない。

NuGetForUnity は依存を「求められる最低の版」で入れるので、依存も最新に上げて `packages.config` に固定している。サーバーは `Directory.Packages.props` で版を一か所に固定し、推移的な依存もその版にそろえる(MagicOnion 7.11.0 が引き込む MessagePack 3.1.7 には既知の脆弱性 GHSA-qhrr-8q5h-9q3h があるので、3.1.12 に上げている)。アプリのサーバーも、同じ版を固定する。`Microsoft.NET.StringTools` は 18 系が .NET 11 向けなので、サーバー(net10)では 17 系の最新を使う。

## ライセンス

MIT。[LICENSE](LICENSE)。
