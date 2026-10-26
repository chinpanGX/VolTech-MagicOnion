# docs

設計と利用ガイドの置き場所。設計・実装・ドキュメントは、このリポジトリの中で行き来しながら更新する。

## 設計

- [C#サーバー基盤 設計書(MagicOnion + PostgreSQL)](design.md): プロジェクト構成、フィルターの順序、エラー設計、ヘッダー、認証・冪等性・トランザクション、Unity クライアント側の変更点

## 計画しているプロジェクト

Unity と共有する `Abstractions` と、サーバー側の全部を持つ `VolTech.Server` の 2 つに分ける。サーバー側の関心ごとはフォルダーで分ける。理由は [設計書](design.md#2-つに分ける理由)。

| プロジェクト / フォルダー | 中身 | 状態 |
|---|---|---|
| `VolTech.Server.Abstractions` | `ErrorCode`、`BusinessException`、ヘッダー名、属性。Unity と共有(netstandard2.1) | あり |
| `VolTech.Server` の `Status/` | サーバー時刻とゲーム内の日付、日付変更の判定 | 未着手 |
| `VolTech.Server` の `Auth/` | `DeviceService`、トークン、`AuthFilter` | 未着手 |
| `VolTech.Server` の `Transaction/` | Npgsql、冪等性、ゲームログ | 未着手 |
| `VolTech.Server` の `Logging/` | ZLogger、`RpcLogFilter` | 未着手 |
| `VolTech.Server` の `Hosting/` | `AddVoltechServer()`。フィルターの順序を固定する | 未着手 |
| Unity クライアント(UPM) | `ApiCore` の MagicOnion 版 | 骨組みだけ(`package.json` と asmdef) |
| `Unity/VolTech.Sandbox` | 開発用の Unity 7 プロジェクト。パッケージを `file:` で参照する | あり |

## 先に確かめること

実装に入る前に、最小のサーバーとクライアントで次を確かめる。

- [ ] サーバーのフィルターで `RpcException(status, trailers)` を投げたとき、`grpc-status-details-bin` がクライアントまで届くか
- [x] クライアントで、呼び出しごとのヘッダーと締め切りを渡し、レスポンスヘッダー(`x-server-time` など)を読めるか。`WithOptions(CallOptions)` と `UnaryResult<T>.ResponseHeadersAsync` で読めた(2026-10-26、Sandbox の `Assets/Smoke/`)
- [ ] iOS の IL2CPP(AOT)で、MagicOnion のクライアントと MessagePack が動くか
- [x] Unity 7 で MagicOnion.Client・MessagePack・YetAnotherHttpHandler が動くか。7000.0.0a12 の Editor(CoreCLR)で、ソースジェネレーターのクライアントから h2c の Unary を呼べた(2026-10-26)。IL2CPP のプレイヤーは未確認。Editor の中では、NuGet で入れた gRPC ではなく Burst が同梱する gRPC 2.61.0(`Editor/Data/Tools/Burst/Client/`)が読み込まれる(アセンブリの版がどれも 2.0.0.0 のため)。Editor での確認は 2.61.0 での結果で、2.84.0 で動くかはプレイヤーのビルドで確かめる
- [ ] 使う版の MagicOnion で、サーバーフィルターの API 名(`GlobalFilters`・`ServiceContext.Items`)が合っているか
