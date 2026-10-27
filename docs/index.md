# docs

| フォルダー | 読む人 | 中身 |
|---|---|---|
| [spec/](spec/index.md) | 基盤を作る人 | 仕様書。基盤が何をするか(振る舞いと、クライアントとの取り決め) |
| [design/](design/index.md) | 基盤を作る人 | 実装設計書。仕様を C# と MagicOnion でどう作るか |
| [guide/](guide/index.md) | 基盤を使う人 | 利用ガイド。アプリへの導入と使い方 |

仕様書を先に直してから、実装設計書とコードを直す。仕様書・実装設計書・コードが食い違ったら、仕様書に合わせる。

## 計画しているプロジェクト

Unity と共有する `Abstractions` と、サーバー側の全部を持つ `VolTech.Server` の 2 つに分ける。サーバー側の関心ごとはフォルダーで分ける。理由は [プロジェクト構成](design/project-structure.md#2-つに分ける理由)。

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

## 確かめること

実装に入る前に、最小のサーバーとクライアントで次を確かめる。

- [ ] サーバー: `IMagicOnionServiceFilter` と `GlobalFilters`、`ServiceContext.Items` が使う版(7.11.0)にあるか
- [ ] サーバーのフィルターで `RpcException(status, trailers)` を投げたとき、`grpc-status-details-bin` がクライアントまで届くか。届かなければ、例外の変換だけを ASP.NET Core gRPC の `Interceptor`(MagicOnion の外側)に移す
- [x] クライアントで、呼び出しごとのヘッダーと締め切りを渡し、レスポンスヘッダー(`x-server-time` など)を読めるか。7.11.0 の `WithOptions(CallOptions)` と `UnaryResult<T>.ResponseHeadersAsync` で読めた(2026-10-26、Sandbox の `Assets/Smoke/`)
- [ ] iOS の IL2CPP(AOT)で、MagicOnion のクライアントと MessagePack が動くか
- [x] Unity 7 で MagicOnion.Client・MessagePack・YetAnotherHttpHandler が動くか。7000.0.0a12 の Editor(CoreCLR)で、ソースジェネレーターのクライアントから h2c の Unary を呼べた(2026-10-26)。IL2CPP のプレイヤーは上の iOS の項目で確かめる。Editor の中では、NuGet で入れた gRPC ではなく Burst が同梱する gRPC 2.61.0(`Editor/Data/Tools/Burst/Client/`)が読み込まれる(アセンブリの版がどれも 2.0.0.0 のため)。Editor での確認は 2.61.0 での結果で、2.84.0 で動くかはプレイヤーのビルドで確かめる
- [x] 使う版を決める。MagicOnion 7.11.0、MessagePack 3.1.12、YetAnotherHttpHandler 1.11.7、gRPC 2.84.0、NuGetForUnity 4.5.0(一覧は [導入](guide/getting-started.md#使う版))

## 決めること

- [ ] DB の最終決定(PostgreSQL + Npgsql + Dapper が推奨)
- [ ] ゲーム内の日付の設定形式(日付が変わる時刻など)。Rust 版の `server_status.toml` のうち、ゲーム日付の部分だけを持つ
- [ ] `StoredAccount` のシリアライズ: protobuf のまま残すか、MessagePack に移すか(移すと保存形式が変わる)
- [ ] Tracing(gRPC Monitor)を MessagePack で作り直すか、リリース外のツールとして割り切るか
- [ ] StreamingHub を使う場合の認証・再接続の設計(ClientFilter は Unary のみ対応)
- [ ] Unity 7 が C# 14 / .NET 10 に対応したら、`Abstractions` の netstandard2.1 / C# 9 の制約を緩める。7.0 のアルファ版の時点では、Unity 6 と同じ C# 9 / .NET Standard 2.1 で、対応の時期は公表されていない
