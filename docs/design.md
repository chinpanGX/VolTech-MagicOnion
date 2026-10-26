# C#サーバー基盤 設計書(MagicOnion + PostgreSQL)

Oct 23, 2026 · @Shota

## 目的と前提

新しいリポジトリ VolTech-MagicOnion で、サーバー基盤と Unity クライアントを C# + MagicOnion で提供する。proto + Rust の VolTech-gRPC と MasterDataTools は今の形のまま残し、3つは互いに参照しない。個人開発の 1 アプリのための基盤なので、Rust 版の機能のうち運用のための機能は落とし、ゲームを動かすのに要る機能だけを C# で再現する。Unity クライアントとの取り決め(エラー形式・ヘッダー)は変えない。

- 確定: MasterMemory、MagicOnion、MessagePack(MasterMemory と MagicOnion が前提にする)
- 確定: Unity は 7 系。基盤の開発用プロジェクトもアプリも Unity 7 で作り、Unity 6 には対応しない
- 推奨(未確定): DB は PostgreSQL + Npgsql + Dapper、マイグレーションは DbUp、ログは ZLogger、テストは xUnit + Testcontainers
- スコープ外: StreamingHub(リアルタイム対戦)、マスターデータの配信フロー、MasterDataTools(別リポジトリ)
- スコープ外(運用の機能): 強制アップデートとメンテナンスのサーバー側の判定、アカウント停止、シャーディング

### 落とす機能と、残す取り決め

落とすのはサーバー側の判定だけにする。予約エラー(`UPDATE_REQUIRED`・`MAINTENANCE`・`ACCOUNT_SUSPENDED`)、ヘッダー(`x-app-platform`・`x-app-version`)、`metadata["end_at"]` は `Abstractions` とクライアントに残す。

強制アップデートは、リリース済みのアプリが `UPDATE_REQUIRED` を受け取って更新を促せないと、後からサーバーに足しても古いアプリには効かない。クライアントが受け口を持ち、バージョンを送ってさえいれば、サーバーの判定は必要になった時点で 1 フィルター足すだけで済む。メンテナンスも同じ。

仕様の根拠は、VolTech-gRPC の `docs/wiki` と `server/rust`、クライアントは `Unity/com.chinpangx.voltech.grpc`(v0.6.0)のソース。VolTech-gRPC のタグから分岐はせず、別の製品として版は 0.1.0 から始める。

## プロジェクト構成

プロジェクトは、Unity と共有するかどうかの 1 本の境界で 2 つに分ける。サーバー側の関心ごと(認証・日付・トランザクションなど)は、プロジェクトではなくフォルダーと名前空間で分ける。

```
VolTech-MagicOnion/
├─ src/
│   ├─ VolTech.Server.Abstractions/    csproj だけ。ソースは Unity パッケージ側を読み込む
│   └─ VolTech.Server/                 サーバー側の全部
├─ tests/
│   ├─ VolTech.Server.Abstractions.Tests/
│   └─ VolTech.Server.Tests/           単体テストと結合テスト(Testcontainers)
└─ Unity/
    ├─ com.chinpangx.voltech.magiconion/
    │   ├─ Runtime/Abstractions/       共有ソースの実体 + asmdef
    │   └─ Runtime/Client/             ApiCore など + asmdef
    └─ VolTech.Sandbox/                開発用の Unity 7 プロジェクト。パッケージを file: で参照する
```

`VolTech.Sandbox` は、パッケージの `.meta` を作る、Unity でコンパイルが通るか確かめる、テストを動かす、IL2CPP でビルドして確かめる、のに使う。基盤の確認がアプリなしで済む。`Unity/Directory.Build.props` は、リポジトリ直下の `Directory.Build.props`(Nullable など)を Unity が作る csproj に効かせないためのもの。

| プロジェクト | 中身 | Unity と共有 |
| --- | --- | --- |
| `VolTech.Server.Abstractions` | `ErrorCode`、予約エラーの定数、`BusinessException`、ヘッダー名の定数、属性(`[SkipAuth]`・`[NoSideEffects]`)、`IApiResponse` | する(netstandard2.1) |
| `VolTech.Server` | 下の表のフォルダーすべて | しない(net10) |

| `VolTech.Server` のフォルダー | 中身 |
| --- | --- |
| `Status/` | サーバー時刻とゲーム内の日付、日付変更の判定、`ServerTimeFilter`・`DateChangeFilter` |
| `Auth/` | `DeviceService`、トークン、`AuthFilter`、`IPlayerResolver` |
| `Transaction/` | Npgsql、冪等性、ゲームログ |
| `Logging/` | ZLogger、`RpcLogFilter` |
| `Hosting/` | `AddVoltechServer()`。フィルターの順序をここで固定する |

### 2 つに分ける理由

最初は Rust 版の 5 クレートと 1 対 1 に分ける案だった。別の製品として作り直すことにしたので、その理由はなくなった。中身を見ると、細かく分けても得るものがない。

- 依存が切れない。`Auth` は端末の登録で DB を使うので `Transaction` に依存し、`Hosting` はすべてに依存する。アプリは `Hosting` を参照するので、結局すべてが入る
- `internal` が使えない。プロジェクトをまたいで型を渡すため、フィルター本体や RPC ごとの設定表まで `public` にするか、`InternalsVisibleTo` を並べることになる。1 つにまとめれば、公開するのは `AddVoltechServer()`・`ITransactions`・`IPlayerResolver`・`AuthContext` の取り出し口などに絞れる
- 本当に必要な境界は「Unity と共有するか」だけ。`Abstractions` は netstandard2.1 で、サーバー専用の依存(Npgsql など)を持ち込めないので、ここだけは別のプロジェクトにする

関心ごとの分離は、`internal` で公開範囲を絞ることと、依存の向きを確かめるテスト(例: `Status` の名前空間は Npgsql を参照しない)で守る。テストは NetArchTest などで書き、ルールをコードとして残す。

### 共有のしかた

- Unity から参照するのは `Abstractions` と `Runtime/Client` だけにする。サーバーのコードは Unity パッケージの外に置くので、Unity に入らない
- `Abstractions` はソースで共有する。`.cs` の実体は UPM パッケージの中(asmdef 付き)に置き、サーバーの csproj はそのフォルダーを `Compile Include` で読み込む。UPM パッケージはパッケージの外のファイルを含められないので、実体はパッケージ側に置く。DLL を配る形と違い、サーバーと Unity のどちらからでも定義へ飛べる
- 今の `src/VolTech.Server.Abstractions/` のソースは、Unity パッケージを作るときにパッケージ側へ移す
- 結合テストが遅くなり CI で分けたくなったら、そのときにテストプロジェクトを分ける

## アプリからの使い方

アプリのリポジトリは `client/`(Unity)・`server/`・`shared/` に分かれている前提で、VolTech-MagicOnion を `external/` の下にサブモジュールとして置く。MasterDataTools など、ほかのサブモジュールも `external/` に集める。

```
MyGame/
├─ client/
│   └─ Packages/manifest.json    "file:../../external/VolTech-MagicOnion/Unity/com.chinpangx.voltech.magiconion"
├─ server/
│   └─ MyGame.Server.csproj      ../external/VolTech-MagicOnion/src/VolTech.Server を参照
├─ shared/                        アプリの IService 定義・レスポンス型(UPM パッケージ + csproj)
└─ external/
    ├─ VolTech-MagicOnion/       サブモジュール
    └─ MasterDataTools/
```

参照の向き:

```
shared → VolTech.Server.Abstractions
server → shared, VolTech.Server
client → shared, VolTech の Unity パッケージ
```

- NuGet で配らず、プロジェクト参照と UPM のローカルパッケージで使う。ソースが手元にあるので、サーバーからも Unity からも基盤の定義へ飛べる
- アプリの `shared/` も `Abstractions` と同じ仕組みにする。`.cs` の実体は UPM パッケージに置き、サーバー側は csproj から読み込む
- フォルダー名を `Packages/` にしないのは、Unity の `client/Packages/` と紛らわしいため
- 使う版は、サブモジュールでタグ(`v0.1.0` など)を checkout して固定する
- アプリのサーバーは、基盤と同じ版の NuGet パッケージ(MagicOnion など)を使う。版が違うとビルドで警告かエラーになるので、基盤が使う版を README に書く
- UPM の `package.json` は NuGet のパッケージを依存に書けない。Unity 側の MagicOnion.Client・MessagePack などは、アプリが NuGetForUnity などで入れる。入れるものは README の手順に書く

## フィルター構成と順序

Rust 版の Layer と同じ順序で、MagicOnion のサーバーフィルターを外側から重ねる。順序を変えると、クライアントとの取り決め(再認証・エラー変換)が崩れる。

1. `RpcLogFilter`(Rust の `RpcLogLayer`): RPC ごとの span とアクセスログ。例外を一元変換する。`BusinessException` は `FAILED_PRECONDITION` + ErrorInfo、想定外の例外は `INTERNAL` にする
2. `ServerTimeFilter`(`ServerStatusLayer` のうちヘッダーの部分): レスポンスに `x-server-time` と `x-game-date` を付ける。DB は使わない。強制アップデートとメンテナンスの判定は持たない(足すときはこのフィルターに入れる)
3. `AuthFilter`(`AuthLayer`): Bearer を検証して `AuthContext` を入れる。`[SkipAuth]` のサービスは通さない
4. `DateChangeFilter`(`DateChangeLayer`): `x-game-date` が今のゲーム内の日付と違えば `DATE_CHANGED` を返す。`AuthFilter` の内側に置く
5. ハンドラー: 本体を `ITransactions.RunAsync` で包む

トランザクションと冪等性をフィルターにしないのは、判定と保存済みレスポンスの書き込みを、本体と同じトランザクションの中で行う必要があるため(Rust 版の `Transactions::run` と同じ理由)。

`AddVoltechServer()` が 1〜4 を `MagicOnionOptions.GlobalFilters` に決まった順で登録し、アプリが順序を入れ替えられないようにする。対象は Unary のみ。StreamingHub は別に設計する。

### MagicOnion で実装するときの注意

- 順序は登録順ではなく `Order` で決まる。アプリがクラスやメソッドに付けたフィルターが基盤より外側に来ないよう、基盤のフィルターには十分小さい `Order`(`int.MinValue` 近く)を明示する
- フィルターのインスタンスはメソッドごとに起動時に作られ、リクエストをまたいで使い回される前提で書く。DB 接続など scoped の依存はコンストラクターで受け取らず、`ServiceContext.ServiceProvider` から取る
- `ServiceContext.MethodInfo` と `AttributeLookup` は実装クラス側の情報なので、共有インターフェースに付けた属性(`[SkipAuth]`・`[NoSideEffects]`)は見えない。起動時にインターフェースマップ(`Type.GetInterfaceMap`)で RPC ごとの設定表を作り、フィルターはそれを引く
- レスポンスヘッダー(`x-server-time`・`x-game-date`)は本文より前に送る必要がある。`ServerTimeFilter` が `next` を呼ぶ前に `CallContext.WriteResponseHeadersAsync` で書く。これで業務エラーのレスポンスにもヘッダーが付く
- サーバーは HTTP/2 で待ち受ける。TLS なし(h2c)で動かす環境では、Kestrel の `Protocols` を `Http2` に明示する(YetAnotherHttpHandler は HTTP/2 で接続する)

## エラー設計

業務エラーは `FAILED_PRECONDITION` に `google.rpc.ErrorInfo` を載せて返す。この形を守れば、クライアントの `ErrorConverter` と `ReservedErrors` は無修正で使える。

| 種類 | ステータス | ErrorInfo |
| --- | --- | --- |
| 業務エラー(日付変更を含む) | `FAILED_PRECONDITION` | アプリの業務エラーはアプリの domain、基盤の予約エラーは domain `voltech` |
| サーバー内部エラー | `INTERNAL` | `voltech` / `INTERNAL_ERROR`。詳細はログにだけ出す |
| 認証エラー | `UNAUTHENTICATED` | なし |

- 運び方: `google.rpc.Status` の details に `ErrorInfo` を入れ、トレーラー `grpc-status-details-bin` で返す。サーバーは `Grpc.StatusProto` で組み立てる(クライアントの手書きパーサーは Unity の都合なので、そのまま残す)
- `metadata["action"]` に `ERROR_ACTION_STAY / RETRY / HOME / TITLE / UPDATE` を入れると、クライアントの既定の action を上書きできる
- `MAINTENANCE` は `metadata["end_at"]` に終了予定(UTC の RFC 3339)を入れる(サーバーが返すのは、メンテナンスの判定を足してから)
- 知らないコードは、古いアプリが `UNKNOWN_ERROR` として扱う。reason を消しても使い回さない

予約エラー(domain `voltech`)は、サーバーが返すものが `INTERNAL_ERROR`・`DATE_CHANGED`。定義だけ残し今は返さないものが `MAINTENANCE`・`UPDATE_REQUIRED`・`ACCOUNT_SUSPENDED`。クライアントが作るものが `NETWORK_ERROR`・`UNKNOWN_ERROR`・`CLIENT_ERROR`・`ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED`。

### C# での実装

- `BusinessException(ErrorCode, message, metadata?)` を `Abstractions` に置く。ハンドラーとフィルターはこれを投げるだけにする
- 最も外側の `RpcLogFilter` が例外を一元変換する。`BusinessException` は `FAILED_PRECONDITION` + ErrorInfo トレーラー、想定外の例外は `INTERNAL`(原因はログにだけ出す)
- `AuthFilter` は `UNAUTHENTICATED` を `ReturnStatusException` で投げる。`RpcLogFilter` は `ReturnStatusException` と `RpcException` を `INTERNAL` に変えず、そのまま通す(変えるとクライアントの再認証が動かない)
- エラーコードは C# の定数として直接書く。予約エラー(domain `voltech`)は `Abstractions` に、アプリの業務エラーはアプリの共有コードに置く。VolTech-gRPC の `errors.toml` と生成ツールは、Rust と C# の両方に同じ定数を作るためのものだった。サーバーもクライアントも C# になり、定数を書いたソースを共有すれば済むので、使わない

## ヘッダーと共通レスポンス項目

ヘッダー名はクライアントの `ApiHeaders` と同じにする。サーバーは `Abstractions` に定数を置き、両方を一緒に直す。

| 向き | ヘッダー | 内容 |
| --- | --- | --- |
| リクエスト | `authorization` | `Bearer <token>`。`[SkipAuth]` のサービスには付かない |
| リクエスト | `x-request-id` | 冪等性の対象(`[NoSideEffects]` の付かない RPC)のみ。1〜64 文字、英数字と `-` |
| リクエスト | `x-app-platform` | ios / android / windows / editor。今はログにだけ使い、強制アップデートを足したらその判定に使う |
| リクエスト | `x-app-version` | リリースごとに増やす整数。用途は `x-app-platform` と同じ |
| リクエスト | `x-game-date` | セッションの最初に受け取った日付(YYYY-MM-DD) |
| レスポンス | `x-server-time` | Unix 時間のミリ秒 |
| レスポンス | `x-game-date` | 今のゲーム内の日付 |

### RPC ごとの設定は属性に置く

Rust 版は proto の `idempotency_level` と `skip_auth` で RPC ごとの扱いを決めていた。C# では共有インターフェースの属性(`[SkipAuth]`、`[NoSideEffects]`)で持つ。`[NoSideEffects]` の付かない RPC は、すべて冪等性の対象にする。サーバーのフィルターとクライアントの `RpcSettings` が同じ属性を読むので、食い違わない。

### 共通レスポンス項目

`player_diff`・バッジ・通知は、`IApiResponse` を実装した MessagePack の型で返す。クライアントの `IPlayerDiffApplier` などは、`IMessage`(protobuf)から `IApiResponse` を受け取る形に変える。

## 認証・冪等性・トランザクション

Rust 版の `auth`・`transaction` クレートと同じ振る舞いを C# で再現する。

### 認証(`Auth/`)

- `DeviceService`(Register / IssueToken)を提供する。認証は不要(`[SkipAuth]`)
- `AuthFilter` が `authorization: Bearer` を検証し、`AuthContext`(device\_id・player\_id・player\_created)を `ServiceContext.Items` に入れる
- 無効・期限切れは `UNAUTHENTICATED`。クライアントはトークンを取り直し、1 回だけ自動で再送する
- プレイヤーの状態は `IPlayerResolver`(NotCreated / Active)をアプリが実装する。アカウント停止(Suspended と `ACCOUNT_SUSPENDED`)は持たない
- トークンの有効期限は既定 1 時間(Rust 版と同じ)

### 冪等性とトランザクション(`Transaction/`)

- ハンドラーの本体を `ITransactions.RunAsync(player, tx => ...)` で包む。成功でコミット、失敗でロールバックする
- 冪等性の対象の RPC(`[NoSideEffects]` の付かないもの)は、同じ `x-request-id` の再送で本体を呼ばず、保存済みのレスポンスを返す
- 保存先は `last_responses(player_id, request_id, rpc, response)`。本体と同じトランザクションで書き、レスポンスは MessagePack のバイト列で持つ。プレイヤー作成時に同じトランザクションで初期行を作る
- `x-request-id` を別の RPC で使い回したら warn を出す。再送で保存済みのレスポンスを返したときは、ゲームログを書かない
- `[NoSideEffects]` の RPC は読み取り専用のトランザクションにする

### ゲームログ

プレイヤーのデータが「いつ・どの RPC で・どう変わったか」を残す。問い合わせを受けて、失ったアイテムや通貨を戻す(復帰処理)ときの根拠にする。アクセスログは RPC が呼ばれたことしか分からず、データがどう変わったかは追えないので、別に持つ。

- 出力先は DB の `game_logs`。本体と同じトランザクションで書く。ロールバックされた処理のログは残らず、コミットされた変更には必ずログがある。ログとデータが食い違わないので、復帰処理の根拠にできる
- `player_id`・`request_id`・RPC・時刻は基盤が付ける。アプリは何が変わったか(種類と内容)だけを渡す
- 再送で保存済みのレスポンスを返したときは書かない。同じ変更が二重に記録されないようにするため

### DB 接続

単一の DB で動かす。シャーディング(`IShardResolver`)は持たず、接続は `NpgsqlDataSource` を DI に 1 つ登録して取る。

## クライアント(Unity)側の変更点

通信の骨格は流用でき、変わるのは `call` の型、レスポンス型の扱い、`DeviceTokenProvider` の通信部分の 3 点。`ApiCoreBuilder` の公開 API は残し、中身だけを差し替えれば、アプリ側のコードは書き直さずに済む。

| 対象 | 扱い | 理由 |
| --- | --- | --- |
| `ApiChannel`(YetAnotherHttpHandler + `GrpcChannel`) | 流用 | `MagicOnionClient.Create<T>(callInvoker)` に同じ CallInvoker を渡せる |
| `ApiCore` の制御(順番待ち、再認証、セッション世代、進行中通知、エラーハンドラー) | 流用 | 通信手段に依存しない |
| `ErrorConverter`、`ErrorPolicy`、`ReservedErrors`、`ErrorHandling/` | 流用 | サーバーが ErrorInfo を同じ形で返す限り無修正 |
| `EncryptedFileAccountStore` などアカウント保存 | 流用 | `StoredAccount` は protobuf のまま残す案が最小(未確定) |
| `ApiHeaders`、`AppVersion`、`ServerClock` | 流用 | ヘッダー名が同じ |
| `ApiCore.InvokeAsync` の `call`(`AsyncUnaryCall<T>`) | 差し替え | `UnaryResult<T>` に変える。`WithOptions(CallOptions)` でヘッダーと締め切りを渡す想定 |
| `IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier` | 差し替え | `IMessage` を `IApiResponse` に変える |
| `DeviceTokenProvider` の通信部分 | 差し替え | 生成された `DeviceGrpc` を MagicOnion の `IDeviceService` に替える |
| `ApiServiceBase` と生成ラッパー、`RpcSettings` | 差し替え | 共有インターフェースの属性から設定を読む |
| Tracing(gRPC Monitor) | 要確認 | protobuf メッセージの記録が前提だと思われる(未読) |
| `protoc-gen-voltech-grpc`(ラッパー生成)、buf の設定、`Runtime/Generated/` の RPC 部分 | 不要 | proto を使わなくなる。エラーコードも C# の定数を共有するので生成しない |

`Google.Protobuf` への依存は残す。エラー詳細(`google.rpc.Status`)の解析とアカウント保存で使っているため。

## 未確定事項と次の作業

実装の前に、MagicOnion の使う版で API を確かめる。ここまでの設計は、次の名前を想定している。

- [ ] サーバー: `IMagicOnionServiceFilter` と `GlobalFilters`、`ServiceContext.Items` が使う版にあるか
- [x] クライアント: `IService<T>.WithOptions(CallOptions)` と `UnaryResult<T>.ResponseHeadersAsync` が使う版にあるか。7.11.0 にあり、Unity 7 の Editor で呼べた
- [ ] `RpcException(status, trailers)` を server filter から投げたとき、`grpc-status-details-bin` がクライアントに届くか。届かなければ、例外の変換だけを ASP.NET Core gRPC の `Interceptor`(MagicOnion の外側)に移す
- [x] 使う版を決める。MagicOnion 7.11.0、MessagePack 3.1.12、YetAnotherHttpHandler 1.11.7、gRPC 2.84.0、NuGetForUnity 4.5.0(一覧は README)
- [ ] iOS の IL2CPP(AOT)で、MagicOnion のクライアントと MessagePack が動くか
- [ ] Unity 7 が C# 14 / .NET 10 に対応したら、`Abstractions` の netstandard2.1 / C# 9 の制約を緩める。7.0 のアルファ版の時点では、Unity 6 と同じ C# 9 / .NET Standard 2.1 で、対応の時期は公表されていない
- [x] Unity 7 で MagicOnion.Client・MessagePack・YetAnotherHttpHandler が動くか。7000.0.0a12 の Editor(CoreCLR)で、ソースジェネレーターのクライアントから h2c の Unary を呼べた。IL2CPP のプレイヤーは上の iOS の項目で確かめる。Editor の中では、NuGet で入れた gRPC ではなく Burst が同梱する gRPC 2.61.0(`Editor/Data/Tools/Burst/Client/`)が読み込まれる(アセンブリの版がどれも 2.0.0.0 のため)。Editor での確認は 2.61.0 での結果で、2.84.0 で動くかはプレイヤーのビルドで確かめる
- [ ] Tracing(gRPC Monitor)を MessagePack で作り直すか、リリース外のツールとして割り切るか
- [ ] `StoredAccount` のシリアライズ: protobuf のまま残すか、MessagePack に移すか(移すと保存形式が変わる)
- [ ] DB の最終決定(PostgreSQL + Npgsql + Dapper が推奨)
- [ ] StreamingHub を使う場合の認証・再接続の設計(ClientFilter は Unary のみ対応)
- [ ] ゲーム内の日付の設定形式(日付が変わる時刻など)。Rust 版の `server_status.toml` のうち、ゲーム日付の部分だけを持つ