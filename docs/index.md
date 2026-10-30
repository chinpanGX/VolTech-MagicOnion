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
| `VolTech.Server.Abstractions` | `ErrorCode`、`ReservedErrors`、`BusinessException`、ヘッダー名、属性、`IDeviceService`。Unity と共有(netstandard2.1)。ソースは Unity パッケージの `Runtime/Abstractions/` | あり |
| `VolTech.Server` の `Status/` | サーバー時刻とゲーム内の日付、日付変更の判定 | あり |
| `VolTech.Server` の `Auth/` | `DeviceService`、トークン、`AuthFilter` | あり |
| `VolTech.Server` の `Transaction/` | `ITransactions`、冪等性、ゲームログ、マイグレーション | あり。共通レスポンス項目(`IApiResponse` とコレクター)は未着手 |
| `VolTech.Server` の `Logging/` | `RpcLogFilter`(アクセスログと例外の変換) | あり。出力形式(ZLogger)はアプリに任せている |
| `VolTech.Server` の `Hosting/` | `AddVoltechServer()`。フィルターの順序を固定する | あり |
| Unity クライアント(UPM) | `ApiCore` の MagicOnion 版 | `Runtime/Client/` に `ApiCore`・`DeviceTokenProvider`・エラー処理・アカウント保存を移した(2026-10-30)。共通レスポンス項目の反映、Tracing、`Runtime/Unity/` の部品(`ApiChannel` 以外)は移していない([実装設計書のクライアント](design/client.md#移していないもの)) |
| `Unity/VolTech.Sandbox` | 開発用の Unity 7 プロジェクト。パッケージを `file:` で参照する | あり |

## 確かめること

実装に入る前に、最小のサーバーとクライアントで次を確かめる。

- [x] サーバー: `IMagicOnionServiceFilter` と `GlobalFilters`、`ServiceContext.Items` が使う版(7.11.0)にあるか。あった。`Order` が小さいほど外側で動く(2026-10-27、`tests/VolTech.Server.Tests` の `FilterTests`)
- [x] サーバーのフィルターで `RpcException(status, trailers)` を投げたとき、`grpc-status-details-bin` がクライアントまで届くか。.NET の MagicOnion クライアントで届いた(2026-10-27、`FilterTests`)
- [ ] Unity(YetAnotherHttpHandler)のクライアントでも `grpc-status-details-bin` が届くか。クライアントを移すときに確かめる
- [x] クライアントで、呼び出しごとのヘッダーと締め切りを渡し、レスポンスヘッダー(`x-server-time` など)を読めるか。7.11.0 の `WithOptions(CallOptions)` と `UnaryResult<T>.ResponseHeadersAsync` で読めた(2026-10-26、Sandbox の `Assets/Smoke/`)
- [x] Windows の IL2CPP でプレイヤーをビルドできるか。`Microsoft.Bcl.AsyncInterfaces` を手で置けば通った(2026-10-28、Sandbox の Windows のビルドプロファイル)。理由と置き方は [導入](guide/getting-started.md#microsoftbclasyncinterfaces-を手で置く)
- [ ] Windows の IL2CPP のプレイヤーから、サーバーを呼べるか(gRPC 2.84.0 とソースジェネレーターのクライアント)
- [ ] iOS の IL2CPP(AOT)で、MagicOnion のクライアントと MessagePack が動くか
- [ ] Unity の IL2CPP で、`Abstractions` の MessagePack の型(`RegisterResult` など)のフォーマッターが生成されるか
- [x] MagicOnion 7.11.0 は、1 つのクラスが `IService<T>` を 2 つ実装しても `ServiceBase<T>` の `T` だけを登録するか。登録した。もう一方の RPC は `Unimplemented` になった(2026-10-30、Uta の外の一時的なテストで確かめた。テストはリポジトリに残していない)。MagicOnion の版を上げたら確かめ直す。サーバーの `RpcSettings` はこの前提で `ServiceBase<T>` の `T` だけを読む([実装設計書のサーバー](design/server.md))
- [ ] Unity の IL2CPP(AOT)で、`ApiServiceBase` が共有インターフェースの属性(`[SkipAuth]`・`[NoSideEffects]`)をリフレクションで読めるか。コードのストリッピングで属性が消えないか。読めなかったら、属性を読む部分をソースジェネレーターに変える([実装設計書のクライアント](design/client.md#ラッパーの書き方))
- [x] Unity 7 で MagicOnion.Client・MessagePack・YetAnotherHttpHandler が動くか。7000.0.0a12 の Editor(CoreCLR)で、ソースジェネレーターのクライアントから h2c の Unary を呼べた(2026-10-26)。IL2CPP のプレイヤーは上の iOS の項目で確かめる。Editor の中では、NuGet で入れた gRPC ではなく Burst が同梱する gRPC 2.61.0(`Editor/Data/Tools/Burst/Client/`)が読み込まれる(アセンブリの版がどれも 2.0.0.0 のため)。Editor での確認は 2.61.0 での結果で、2.84.0 で動くかはプレイヤーのビルドで確かめる
- [x] 使う版を決める。MagicOnion 7.11.0、MessagePack 3.1.12、YetAnotherHttpHandler 1.11.7、gRPC 2.84.0、NuGetForUnity 4.5.0(一覧は [導入](guide/getting-started.md#使う版))

## 決めること

- [x] DB の最終決定。PostgreSQL + Npgsql、マイグレーションは DbUp。基盤は Dapper に依存しない(アプリが使うのは自由)(2026-10-27)
- [x] ゲーム内の日付の設定形式。`VoltechServerOptions.GameDay`(`ResetHour`・`TimeZone`)を ASP.NET Core の設定から入れる(2026-10-27)
- [ ] 共通レスポンス項目(`IApiResponse`)と、`player_diff`・バッジ・通知を集める仕組み(Rust 版のコレクター)
- [ ] ログの出力形式。基盤は `ILogger` のスコープとアクセスログを出すだけで、JSON での出力(ZLogger)はアプリに任せている。基盤で決めるか
- [ ] `StoredAccount` のシリアライズ: protobuf のまま残すか、MessagePack に移すか(移すと保存形式が変わる)。今は VolTech-gRPC 版のまま変えていない
- [ ] Tracing(gRPC Monitor)を MessagePack で作り直すか、リリース外のツールとして割り切るか
- [ ] StreamingHub を使う場合の認証・再接続の設計(ClientFilter は Unary のみ対応)
- [ ] Unity 7 が C# 14 / .NET 10 に対応したら、`Abstractions` の netstandard2.1 / C# 9 の制約を緩める。7.0 のアルファ版の時点では、Unity 6 と同じ C# 9 / .NET Standard 2.1 で、対応の時期は公表されていない

## 進め方

サーバー側(`VolTech.Server`)は、5 つのフォルダーをすべて実装した(2026-10-27)。テストは `Abstractions` が 25 件、`VolTech.Server` が 41 件(依存の向きのテスト 3 件を含む)で、すべて通る(2026-10-30、`dotnet test`。PostgreSQL のテストは Docker が要る)。残りは次の順に進める。

1. Windows の IL2CPP のプレイヤーで、gRPC 2.84.0 とソースジェネレーターのクライアント、`Abstractions` の MessagePack の型が動くか確かめる(ビルドは通った)。iOS は Mac が要る
2. Unity クライアントの `ApiCore` を MagicOnion 版にする([実装設計書のクライアント](design/client.md))。移した(2026-10-30。Sandbox でコンパイルと EditMode のテスト 48 件が通った)。共通レスポンス項目の反映は 3 が決まってから足す。サーバーを相手にした確認(上の YetAnotherHttpHandler のトレーラー、IL2CPP)は残っている
3. 共通レスポンス項目(`IApiResponse`)と、`player_diff`・バッジ・通知を集める仕組みを足す。先に仕様書と実装設計書に書く。足したら、クライアントの `ApiCore` に反映の処理(VolTech-gRPC 版の `IPlayerDiffApplier` など)を戻す
4. 依存の向きのテスト(例: `Auth/`・`Transaction/` 以外は Npgsql を参照しない)を足す。足した(2026-10-30、NetArchTest。ルールは [プロジェクト構成](design/project-structure.md#2-つに分ける理由))

確認に使ったクライアントは Sandbox の `Assets/Smoke/` にある。`ISmokeService.cs`(`Assets/Smoke/Shared/`)はサーバーとクライアントの両方で読み込む前提で、相手のサーバーはまだリポジトリに入れていない(一時的に作って確かめた)。
