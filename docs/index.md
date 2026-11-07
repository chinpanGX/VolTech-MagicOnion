# docs

| フォルダー | 読む人 | 中身 |
|---|---|---|
| [designs/](designs/index.md) | 基盤を作る人 | egm の設計書。機能ごとに、振る舞いと作り方を 1 つの文書に書く。新しい機能・変更はここに書く |
| [spec/](spec/index.md) | 基盤を作る人 | 仕様書。基盤が何をするか(振る舞いと、クライアントとの取り決め) |
| [designs/implementation/](designs/implementation/index.md) | 基盤を作る人 | 実装設計書。仕様を C# と MagicOnion でどう作るか |
| [guide/](guide/index.md) | 基盤を使う人 | 利用ガイド。アプリへの導入と使い方 |

新しい機能・変更は、egm(`/egm:egm-draft`・`/egm:egm-review`)で `designs/` に設計書を書き、承認してからコードを直す。食い違ったら、`designs/` の承認済みの設計書 → 仕様書 → 実装設計書 → コードの順に正しい。`spec/`・`designs/implementation/` は、`designs/` がまだ扱っていない部分の正として残す。

## 計画しているプロジェクト

Unity と共有する `Abstractions` と、サーバー側の全部を持つ `VolTech.Server` の 2 つに分ける。サーバー側の関心ごとはフォルダーで分ける。理由は [プロジェクト構成](designs/implementation/project-structure.md#2-つに分ける理由)。

| プロジェクト / フォルダー | 中身 | 状態 |
|---|---|---|
| `VolTech.Server.Abstractions` | `ErrorCode`、`ReservedErrors`、`BusinessException`、ヘッダー名、属性、`IDeviceService`。Unity と共有(netstandard2.1)。ソースは Unity パッケージの `Runtime/Abstractions/` | あり |
| `VolTech.Server` の `Status/` | サーバー時刻とゲーム内の日付、日付変更の判定 | あり |
| `VolTech.Server` の `Auth/` | `DeviceService`、トークン、`AuthFilter` | あり |
| `VolTech.Server` の `Transaction/` | `ITransactions`、冪等性、ゲームログ、マイグレーション | あり。共通レスポンス項目(`ICommonResponseCollector<TCommon>`・`AddCommonResponse`)もあり(2026-11-03) |
| `VolTech.Server` の `Logging/` | `RpcLogFilter`(アクセスログと例外の変換) | あり。出力形式(ZLogger)はアプリに任せている |
| `VolTech.Server` の `Hosting/` | `AddVoltechServer()`。フィルターの順序を固定する | あり |
| Unity クライアント(UPM) | `ApiCore` の MagicOnion 版 | `Runtime/Client/` に `ApiCore`・`DeviceTokenProvider`・エラー処理・アカウント保存を移した(2026-10-30)。共通レスポンス項目の反映(`WithCommonResponseApplier`)と、公式の呼び方(`CreateClient<T>`・`ToResult`)は [設計書 1](designs/common-response-and-client-filter.md)。Monitor(`Editor/`)は [設計書 4](designs/api-monitor.md)(2026-11-08)。`Runtime/Unity/` の部品(`ApiChannel` 以外)は移していない([実装設計書のクライアント](designs/implementation/client.md#移していないもの)) |
| `Unity/VolTech.Sandbox` | 開発用の Unity 7 プロジェクト。パッケージを `file:` で参照する | あり。`Assets/Sample` にガチャと日付更新のサンプルがある(相手は `samples/VolTech.Sandbox.Server`) |

## 確かめること

実装に入る前に、最小のサーバーとクライアントで次を確かめる。

- [x] サーバー: `IMagicOnionServiceFilter` と `GlobalFilters`、`ServiceContext.Items` が使う版(7.11.0)にあるか。あった。`Order` が小さいほど外側で動く(2026-10-27、`tests/VolTech.Server.Tests` の `FilterTests`)
- [x] サーバーのフィルターで `RpcException(status, trailers)` を投げたとき、`grpc-status-details-bin` がクライアントまで届くか。.NET の MagicOnion クライアントで届いた(2026-10-27、`FilterTests`)
- [x] Unity(YetAnotherHttpHandler)のクライアントでも `grpc-status-details-bin` が届くか。届いた(2026-11-07、PlayMode の `E2ETests` で `tests/VolTech.E2E.Server` を呼んだ。7000.0.0a12 の Editor)
- [x] Unity のクライアントが本物のサーバーと通信できるか。端末登録、トークン、ヘッダー、再認証、再送、エラー、`Common`、キャンセル、締め切りを確かめた(2026-11-07、同じテスト 12 件。Editor では gRPC 2.61.0)
- [x] Sandbox のサンプルの構成が動くか。`includePlatforms` が空で `UNITY_EDITOR` が条件の asmdef の MonoBehaviour をシーンに付けられた。テストは `--mode PlayMode --filter` で動いた。`LegacyRuntime.ttf` の `Text` に日本語の文字がある(2026-11-07、Editor の PlayMode で確かめた)
- [x] Sandbox のサンプルを Editor で動かす。CUJ 1〜3 と、サーバーが止まっているときの帯、再試行、Server Clock ウィンドウを確かめた(2026-11-07、開いている Editor を Unity Pipeline で操作。ボタンは Input System の仮想の Mouse で押し、ウィンドウは `SendEvent` で押した。OS の本物のマウスでは押していない)
- [x] クライアントで、呼び出しごとのヘッダーと締め切りを渡し、レスポンスヘッダー(`x-server-time` など)を読めるか。7.11.0 の `WithOptions(CallOptions)` と `UnaryResult<T>.ResponseHeadersAsync` で読めた(2026-10-26、Sandbox の `Assets/Smoke/`)
- [x] Windows の IL2CPP でプレイヤーをビルドできるか。`Microsoft.Bcl.AsyncInterfaces` を手で置けば通る。ただし `.meta` に `Exclude Editor` を書く古い形式だと DLL がプレイヤーに渡らず `IL1009` で失敗する。`Any` を有効・`Editor` を無効にする形式に直して通った(2026-11-01、Sandbox のコピーを `BuildPipeline.BuildPlayer` で IL2CPP に指定。7000.0.0a12)。2026-10-28 の記録は、そのときの `.meta` の形を確かめておらず、再現できなかった。理由と置き方は [導入](guide/getting-started.md#microsoftbclasyncinterfaces-を手で置く)
- [ ] Windows の IL2CPP のプレイヤーから、サーバーを呼べるか(gRPC 2.84.0 とソースジェネレーターのクライアント)
- [ ] iOS の IL2CPP(AOT)で、MagicOnion のクライアントと MessagePack が動くか
- [ ] Unity の IL2CPP で、`Abstractions` の MessagePack の型(`RegisterResult` など)のフォーマッターが生成されるか
- [x] MagicOnion 7.11.0 は、1 つのクラスが `IService<T>` を 2 つ実装しても `ServiceBase<T>` の `T` だけを登録するか。登録した。もう一方の RPC は `Unimplemented` になった(2026-10-30、Uta の外の一時的なテストで確かめた。テストはリポジトリに残していない)。MagicOnion の版を上げたら確かめ直す。サーバーの `RpcSettings` はこの前提で `ServiceBase<T>` の `T` だけを読む([実装設計書のサーバー](designs/implementation/server.md))
- [ ] Unity の IL2CPP(AOT)で、`RpcSettings` が共有インターフェースの属性(`[SkipAuth]`・`[NoSideEffects]`)をリフレクションで読めるか。コードのストリッピングで属性が消えないか
- [x] MagicOnion のクライアントの `IClientFilter` と `CallInvoker` の動き(`next` の再呼び出し、`next` の中で `CallInvoker` まで進むこと、`Metadata` の使い回し、`WithHeaders`、例外の型、`TResponse`)。ソースジェネレーターのクライアントで確かめた(2026-11-07、EditMode の `ClientBehaviorTests`、7000.0.0a12 の Editor)
- [x] `[MagicOnionClientGeneration]` は、書いた型のアセンブリ全体を生成の対象にする。対象でないアセンブリは `MagicOnionClient.Create<T>` が `NotSupportedException` になる。基底インターフェースから受け継いだメソッドは実装が作られず、コンパイルエラーになる(2026-11-07、同じテスト)
- [x] ソースジェネレーターのクライアントで、`CallInvoker` の `TRequest` が参照型ならその型、引数なしが `Box<Nil>`、値型 1 つが `Box<T>`、複数の引数が `Box<DynamicArgumentTuple<...>>` になるか。なった。`Marshaller` の `ContextualSerializer`・`ContextualDeserializer` で往復でき、`Interceptor` と `ArrayBufferWriter<byte>` も使える(2026-11-08、EditMode の `ClientBehaviorTests`、7000.0.0a12 の Editor)
- [x] Unity 7 の `DEVELOPMENT_BUILD`。定義されなくなった。`ScriptCompilationOptions.DevelopmentBuild` も使えない(2026-11-08、7000.0.0a12 のコンパイルエラーのメッセージ)。開発ビルドだけに入れる部品を作るときは、方法から決め直す
- [x] Unity 7 で MagicOnion.Client・MessagePack・YetAnotherHttpHandler が動くか。7000.0.0a12 の Editor(CoreCLR)で、ソースジェネレーターのクライアントから h2c の Unary を呼べた(2026-10-26)。IL2CPP のプレイヤーは上の iOS の項目で確かめる。Editor の中では、NuGet で入れた gRPC ではなく Burst が同梱する gRPC 2.61.0(`Editor/Data/Tools/Burst/Client/`)が読み込まれる(アセンブリの版がどれも 2.0.0.0 のため)。Editor での確認は 2.61.0 での結果で、2.84.0 で動くかはプレイヤーのビルドで確かめる
- [x] 使う版を決める。MagicOnion 7.11.0、MessagePack 3.1.12、YetAnotherHttpHandler 1.11.7、gRPC 2.84.0、NuGetForUnity 4.5.0(一覧は [導入](guide/getting-started.md#使う版))

## 決めること

- [x] DB の最終決定。PostgreSQL + Npgsql、マイグレーションは DbUp。基盤は Dapper に依存しない(アプリが使うのは自由)(2026-10-27)
- [x] ゲーム内の日付の設定形式。`VoltechServerOptions.GameDay`(`ResetHour`・`TimeZone`)を ASP.NET Core の設定から入れる(2026-10-27)
- [x] 共通レスポンス項目(`IApiResponse`)と、`player_diff`・バッジ・通知を集める仕組み(Rust 版のコレクター)。アプリが 1 つの型 `TCommon` にまとめ、レスポンスの型が `IApiResponse<TCommon>` を実装する(戻り値の型は変えない)。サーバーは `ICommonResponseCollector<TCommon>` をトランザクションごとに作り、`RunAsync` が本体の後に詰める。クライアントの反映処理は `ICommonResponseApplier<TCommon>` 1 つ(2026-11-07。[設計書 1](designs/common-response-and-client-filter.md)、[共通レスポンス項目](spec/common-response.md))
- [ ] ログの出力形式。基盤は `ILogger` のスコープとアクセスログを出すだけで、JSON での出力(ZLogger)はアプリに任せている。基盤で決めるか
- [ ] `StoredAccount` のシリアライズ: protobuf のまま残すか、MessagePack に移すか(移すと保存形式が変わる)。今は VolTech-gRPC 版のまま変えていない
- [x] Tracing(gRPC Monitor)を MessagePack で作り直すか。作る(2026-11-04、[RFC](designs/foundation-direction.md))。設計と実装は [Monitor の設計書](designs/api-monitor.md)(2026-11-08)。実機の表示は後の段階
- [ ] StreamingHub を使う場合の認証・再接続の設計(ClientFilter は Unary のみ対応)
- [ ] Unity 7 が C# 14 / .NET 10 に対応したら、`Abstractions` の netstandard2.1 / C# 9 の制約を緩める。7.0 のアルファ版の時点では、Unity 6 と同じ C# 9 / .NET Standard 2.1 で、対応の時期は公表されていない

## 進め方

サーバー側(`VolTech.Server`)は、5 つのフォルダーをすべて実装した(2026-10-27)。テストは `Abstractions` が 25 件、`VolTech.Server` が 51 件(依存の向きのテスト 3 件、共通レスポンス項目の 8 件を含む)で、すべて通る(2026-11-03、`dotnet vstest`。PostgreSQL のテストは Docker が要る)。残りは次の順に進める。

1. Windows の IL2CPP のプレイヤーで、gRPC 2.84.0 とソースジェネレーターのクライアント、`Abstractions` の MessagePack の型が動くか確かめる(ビルドは通った)。iOS は Mac が要る
2. Unity クライアントの `ApiCore` を MagicOnion 版にする([実装設計書のクライアント](designs/implementation/client.md))。移した(2026-10-30。Sandbox でコンパイルと EditMode のテスト 50 件が通った)。共通レスポンス項目の反映は 3 で足した。サーバーを相手にした確認(上の YetAnotherHttpHandler のトレーラー、IL2CPP)は残っている
3. 共通レスポンス項目(`IApiResponse`)と、`player_diff`・バッジ・通知を集める仕組みを足す。足した(2026-11-03。サーバーは `AddCommonResponse`・`TxContext.GetCollector`、クライアントは `IPlayerDiffApplier<TCommon>` など。[共通レスポンス項目](spec/common-response.md))
4. 依存の向きのテスト(例: `Auth/`・`Transaction/` 以外は Npgsql を参照しない)を足す。足した(2026-10-30、NetArchTest。ルールは [プロジェクト構成](designs/implementation/project-structure.md#2-つに分ける理由))

確認に使ったクライアントは Sandbox の `Assets/Smoke/` にある。`ISmokeService.cs`(`Assets/Smoke/Shared/`)はサーバーとクライアントの両方で読み込む前提で、相手のサーバーはまだリポジトリに入れていない(一時的に作って確かめた)。
