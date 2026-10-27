# docs

設計と利用ガイドの置き場所。設計・実装・ドキュメントは、このリポジトリの中で行き来しながら更新する。

## 設計

- [C#サーバー基盤 設計書(MagicOnion + PostgreSQL)](design.md): プロジェクト構成、フィルターの順序、エラー設計、ヘッダー、認証・冪等性・トランザクション、Unity クライアント側の変更点

## 計画しているプロジェクト

Unity と共有する `Abstractions` と、サーバー側の全部を持つ `VolTech.Server` の 2 つに分ける。サーバー側の関心ごとはフォルダーで分ける。理由は [設計書](design.md#2-つに分ける理由)。

| プロジェクト / フォルダー | 中身 | 状態 |
|---|---|---|
| `VolTech.Server.Abstractions` | `ErrorCode`、`ReservedErrors`、`BusinessException`、ヘッダー名、属性、`IDeviceService`。Unity と共有(netstandard2.1)。ソースは Unity パッケージの `Runtime/Abstractions/` | あり |
| `VolTech.Server` | サーバー側の全部。下のフォルダーに分ける | あり |
| `VolTech.Server` の `Status/` | サーバー時刻とゲーム内の日付、日付変更の判定 | あり |
| `VolTech.Server` の `Auth/` | `DeviceService`、トークン、`AuthFilter` | あり |
| `VolTech.Server` の `Transaction/` | `ITransactions`、冪等性、ゲームログ、マイグレーション | あり。共通レスポンス項目(コレクター)は未着手 |
| `VolTech.Server` の `Logging/` | `RpcLogFilter`(アクセスログと例外の変換) | あり。出力形式(ZLogger)はアプリに任せている |
| `VolTech.Server` の `Hosting/` | `AddVoltechServer()`。フィルターの順序を固定する | あり |
| Unity クライアント(UPM) | `ApiCore` の MagicOnion 版 | 骨組みだけ(`package.json` と asmdef) |
| `Unity/VolTech.Sandbox` | 開発用の Unity 7 プロジェクト。パッケージを `file:` で参照する | あり |

## 先に確かめること

実装に入る前に、最小のサーバーとクライアントで次を確かめる。

- [x] サーバーのフィルターで `RpcException(status, trailers)` を投げたとき、`grpc-status-details-bin` がクライアントまで届くか。.NET の MagicOnion クライアントで届いた(2026-10-27、`tests/VolTech.Server.Tests` の `FilterTests`)。Unity のクライアントでは、クライアントを移すときに確かめる
- [x] クライアントで、呼び出しごとのヘッダーと締め切りを渡し、レスポンスヘッダー(`x-server-time` など)を読めるか。`WithOptions(CallOptions)` と `UnaryResult<T>.ResponseHeadersAsync` で読めた(2026-10-26、Sandbox の `Assets/Smoke/`)
- [ ] iOS の IL2CPP(AOT)で、MagicOnion のクライアントと MessagePack が動くか
- [x] Unity 7 で MagicOnion.Client・MessagePack・YetAnotherHttpHandler が動くか。7000.0.0a12 の Editor(CoreCLR)で、ソースジェネレーターのクライアントから h2c の Unary を呼べた(2026-10-26)。IL2CPP のプレイヤーは未確認。Editor の中では、NuGet で入れた gRPC ではなく Burst が同梱する gRPC 2.61.0(`Editor/Data/Tools/Burst/Client/`)が読み込まれる(アセンブリの版がどれも 2.0.0.0 のため)。Editor での確認は 2.61.0 での結果で、2.84.0 で動くかはプレイヤーのビルドで確かめる
- [x] 使う版の MagicOnion で、サーバーフィルターの API 名(`GlobalFilters`・`ServiceContext.Items`)が合っているか。7.11.0 で合っていた。`Order` が小さいほど外側で動く(2026-10-27)

## 進め方

サーバー側(`VolTech.Server`)は、設計書の 5 つのフォルダーをすべて実装した(2026-10-27)。テストは DB を使わないもの(フィルター・エラーの形・ヘッダー)と、PostgreSQL をコンテナで動かすもの(認証・冪等性・ゲームログ)がある。残りは次の順に進める。

1. Windows の IL2CPP でプレイヤーをビルドし、gRPC 2.84.0 とソースジェネレーターのクライアント、`Abstractions` の MessagePack の型が動くか確かめる。iOS は Mac が要る
2. Unity クライアントの `ApiCore` を MagicOnion 版にする([設計書](design.md#クライアントunity側の変更点))。`DeviceTokenProvider` は `IDeviceService` を呼ぶ形にする
3. 共通レスポンス項目(`IApiResponse`)と、`player_diff`・バッジ・通知を集める仕組みを足す
4. 依存の向きのテスト(例: `Status` の名前空間は Npgsql を参照しない)を足す

確認に使ったクライアントは Sandbox の `Assets/Smoke/` にある。`ISmokeService.cs`(`Assets/Smoke/Shared/`)はサーバーとクライアントの両方で読み込む前提で、相手のサーバーはまだリポジトリに入れていない(一時的に作って確かめた)。
