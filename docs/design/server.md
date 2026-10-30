# サーバー

## フィルター

[リクエストの処理順](../spec/processing-order.md) の 1〜4 を、MagicOnion のサーバーフィルターとして外側から重ねる。5 の本体はフィルターにしない。

| 順 | フィルター | Rust 版 | フォルダー |
| --- | --- | --- | --- |
| 1 | `RpcLogFilter` | `RpcLogLayer` | `Logging/` |
| 2 | `ServerTimeFilter` | `ServerStatusLayer`(ヘッダーの部分) | `Status/` |
| 3 | `AuthFilter` | `AuthLayer` | `Auth/` |
| 4 | `DateChangeFilter` | `DateChangeLayer` | `Status/` |
| 5 | ハンドラー(`ITransactions.RunAsync` で包む) | `Transactions::run` | `Transaction/` |

`AddVoltechServer()` が 1〜4 を `MagicOnionOptions.GlobalFilters` に決まった順で登録し、アプリが順序を入れ替えられないようにする。`Order` は `int.MinValue` から順に 1 ずつ増やす(`VoltechServerExtensions.FilterOrder`)。アプリのフィルター(`Order` の既定は `int.MaxValue`)は、必ず基盤より内側で動く。

トランザクションと冪等性をフィルターにしないのは、判定と保存済みレスポンスの書き込みを、本体と同じトランザクションの中で行う必要があるため(Rust 版の `Transactions::run` と同じ理由)。

### MagicOnion で実装するときの注意

- `IMagicOnionServiceFilter`・`GlobalFilters`・`ServiceContext.Items` は 7.11.0 にある。`ServiceContext.ServiceName`・`MethodName` で RPC の名前も読めるので、Rust 版の `RpcMethodLayer` に当たる部品は要らない
- 順序は登録順ではなく `Order` で決まる(小さいほど外側。テストで確かめた)。アプリがクラスやメソッドに付けたフィルターが基盤より外側に来ないよう、基盤のフィルターには十分小さい `Order`(`int.MinValue` 近く)を明示する
- フィルターのインスタンスはメソッドごとに起動時に作られ、リクエストをまたいで使い回される前提で書く。DB 接続など scoped の依存はコンストラクターで受け取らず、`ServiceContext.ServiceProvider` から取る
- `ServiceContext.MethodInfo` と `AttributeLookup` は実装クラス側の情報なので、共有インターフェースに付けた属性(`[SkipAuth]`・`[NoSideEffects]`)は見えない。インターフェースマップ(`Type.GetInterfaceMap`)で実装のメソッドから共有インターフェースのメソッドを引き、その属性で RPC ごとの設定(`Internal/RpcSettings`)を作る。メソッドごとに 1 回だけ作ってキャッシュし、フィルターはそれを引く
- 読むのは共有インターフェースとそのメソッドの属性だけ。実装クラスと実装メソッドの属性は読まない([ヘッダーと RPC ごとの設定](../spec/headers.md#rpc-ごとの設定は属性に置く))。`SkipAuthAttribute` の `AttributeUsage` は `Interface | Method` にして、クラスには付けられないようにする
- 共有インターフェースは、サービスのクラスの基底をたどって見つけた `ServiceBase<T>` の `T` の 1 つだけにし、`GetInterfaceMap(T)` だけを引く。実装クラスのすべてのインターフェースから最初に当たったものを使うと、同じシグネチャのメソッドを持つ別のインターフェースが先に並んだとき、その属性を読んでしまう
- MagicOnion 7.11.0 は、1 つのクラスが `IService<T>` を 2 つ実装しても `ServiceBase<T>` の `T` だけを登録し、もう一方の RPC は `Unimplemented` になる。実装を基底クラスに置いても、`ServiceContext.MethodInfo` とインターフェースマップのメソッドは一致し、属性を読める(2026-10-30、一時的なテストで確かめた)。そのため、共有インターフェースが 2 つ以上になる場合は考えない
- `ServiceBase<T>` が見つからない、またはインターフェースマップにメソッドが無いとき(MagicOnion のサービスなら起きない)は、属性が無いものとして既定の設定(認証が要る・冪等性の対象)にする
- レスポンスヘッダー(`x-server-time`・`x-game-date`)は本文より前に送る必要がある。`ServerTimeFilter` が `next` を呼ぶ前に `CallContext.WriteResponseHeadersAsync` で書く。これで業務エラーのレスポンスにもヘッダーが付く
- TLS なし(h2c)で動かす環境では、Kestrel の `Protocols` を `Http2` に明示する

## エラーの変換

[エラー](../spec/errors.md) の形を、次のように作る。

- `BusinessException(ErrorCode, message, metadata?)` を `Abstractions` に置く。ハンドラーとフィルターはこれを投げるだけにする
- 最も外側の `RpcLogFilter` が例外を一元変換する。`BusinessException` は `FAILED_PRECONDITION` + ErrorInfo トレーラー、想定外の例外は `INTERNAL`(原因はログにだけ出す)
- トレーラー `grpc-status-details-bin` は `Grpc.StatusProto` で組み立てる(クライアントの手書きパーサーは Unity の都合なので、そのまま残す)
- トレーラーは、フィルターから投げても .NET の MagicOnion クライアントまで届く(`tests/VolTech.Server.Tests` の `FilterTests`)。Unity(YetAnotherHttpHandler)で届くかは、クライアントを移すときに確かめる
- `AuthFilter` は `UNAUTHENTICATED` を `ReturnStatusException` で投げる。`RpcLogFilter` は `ReturnStatusException` と `RpcException` を `INTERNAL` に変えず、そのまま通す
- エラーコードは C# の定数として直接書く。VolTech-gRPC の `errors.toml` と生成ツールは、Rust と C# の両方に同じ定数を作るためのものだった。サーバーもクライアントも C# になり、定数を書いたソースを共有すれば済むので、使わない

## 認証

- `AuthFilter` が検証した結果の `AuthContext` を `ServiceContext.Items` に入れる。ハンドラーは `Context.GetAuthContext()` で取り出す
- トークンの有効期限は `VoltechServerOptions.AccessTokenLifetime`(既定 1 時間)
- トークンは 256 ビットの乱数の 16 進(小文字 64 文字)。期限は DB の時刻ではなく `TimeProvider` で比べる(テストで時刻を進められる)
- secretKey は PBKDF2-SHA256(.NET 標準)でハッシュにして保存する。Rust 版は Argon2 だったが、secretKey はクライアントが作る 16〜128 文字の乱数で、人が覚えるパスワードではないので、遅いハッシュで総当たりに備える必要がない。依存を増やさないほうを選んだ
- device_id と player_id は ULID(Cysharp の `Ulid`)。DB には player_id を `uuid` で持つ

## ゲーム内の日付

- 設定は `VoltechServerOptions.GameDay`(`ResetHour`・`TimeZone`)。ASP.NET Core の設定(`appsettings.json` など)から入れ、`IOptionsMonitor` で読むので、ファイルを書き換えると読み直す。書き間違いは起動時に検証して止める
- ハンドラーは `Context.GetGameDate()` で今のゲーム内の日付を取る

## 冪等性とトランザクション

- 保存先は `last_responses(player_id, request_id, rpc, response)`。レスポンスは MessagePack のバイト列(MagicOnion と同じシリアライザー)で持つ
- 行が無ければ、`RunAsync` が同じトランザクションの中で作ってからロックする(`INSERT ... ON CONFLICT DO NOTHING` → `SELECT ... FOR UPDATE`)。Rust 版(MySQL)は隙間のロックによるデッドロックを避けるため別のトランザクションで作っていたが、PostgreSQL は隙間のロックを取らないので要らない
- `[NoSideEffects]` の RPC は `SET TRANSACTION READ ONLY` で動かす

## ゲームログ

- 形は Rust 版と同じ。数量の変化は `GameLog.Quantity(kind, delta, after, reason)`、それ以外は `GameLog.Event(kind, reason)` で作り、`Target(id)` と `Field(name, value)` を足す。種別で変わる値は `body`(jsonb)にまとめる
- ハンドラーは `tx.WriteGameLog(...)` で書く

## ログ

- `RpcLogFilter` は `ILogger` のスコープ(`rpc.method`・`request_id`・`player_id`・`replayed`)とアクセスログ(`rpc finished`)を出す。JSON での出力(ZLogger)はアプリの設定に任せている(基盤で決めるかは [未決](../index.md#決めること))

## DB 接続

接続は `NpgsqlDataSource` を DI に 1 つ登録して取る。シャーディング(`IShardResolver`)は持たない。

基盤のテーブル(`devices`・`access_tokens`・`last_responses`・`game_logs`)は `VoltechDatabase.Migrate()` が DbUp で作る。SQL は `Transaction/Migrations/` に埋め込みリソースとして置く。適用済みの記録は `_voltech_migrations` に入れ、アプリのマイグレーションの記録とは分ける(混ぜると、互いに相手の分を知らないマイグレーションとして扱う)。基盤は SQL を Npgsql で直接書き、Dapper には依存しない。アプリは `TxContext.Connection`・`Transaction` を Dapper に渡して使える。

## テスト

`tests/VolTech.Server.Tests` は、DB を使わないもの(フィルターの順序・エラーの形・ヘッダー)と、PostgreSQL をコンテナで動かすもの(認証・冪等性・ゲームログ)に分かれる。後者は Testcontainers を使うので Docker が要る。
