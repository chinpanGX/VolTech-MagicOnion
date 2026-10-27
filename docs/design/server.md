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

`AddVoltechServer()` が 1〜4 を `MagicOnionOptions.GlobalFilters` に決まった順で登録し、アプリが順序を入れ替えられないようにする。

トランザクションと冪等性をフィルターにしないのは、判定と保存済みレスポンスの書き込みを、本体と同じトランザクションの中で行う必要があるため(Rust 版の `Transactions::run` と同じ理由)。

### MagicOnion で実装するときの注意

- 順序は登録順ではなく `Order` で決まる。アプリがクラスやメソッドに付けたフィルターが基盤より外側に来ないよう、基盤のフィルターには十分小さい `Order`(`int.MinValue` 近く)を明示する
- フィルターのインスタンスはメソッドごとに起動時に作られ、リクエストをまたいで使い回される前提で書く。DB 接続など scoped の依存はコンストラクターで受け取らず、`ServiceContext.ServiceProvider` から取る
- `ServiceContext.MethodInfo` と `AttributeLookup` は実装クラス側の情報なので、共有インターフェースに付けた属性(`[SkipAuth]`・`[NoSideEffects]`)は見えない。起動時にインターフェースマップ(`Type.GetInterfaceMap`)で RPC ごとの設定表を作り、フィルターはそれを引く
- レスポンスヘッダー(`x-server-time`・`x-game-date`)は本文より前に送る必要がある。`ServerTimeFilter` が `next` を呼ぶ前に `CallContext.WriteResponseHeadersAsync` で書く。これで業務エラーのレスポンスにもヘッダーが付く
- TLS なし(h2c)で動かす環境では、Kestrel の `Protocols` を `Http2` に明示する

## エラーの変換

[エラー](../spec/errors.md) の形を、次のように作る。

- `BusinessException(ErrorCode, message, metadata?)` を `Abstractions` に置く。ハンドラーとフィルターはこれを投げるだけにする
- 最も外側の `RpcLogFilter` が例外を一元変換する。`BusinessException` は `FAILED_PRECONDITION` + ErrorInfo トレーラー、想定外の例外は `INTERNAL`(原因はログにだけ出す)
- トレーラー `grpc-status-details-bin` は `Grpc.StatusProto` で組み立てる(クライアントの手書きパーサーは Unity の都合なので、そのまま残す)
- `AuthFilter` は `UNAUTHENTICATED` を `ReturnStatusException` で投げる。`RpcLogFilter` は `ReturnStatusException` と `RpcException` を `INTERNAL` に変えず、そのまま通す
- エラーコードは C# の定数として直接書く。VolTech-gRPC の `errors.toml` と生成ツールは、Rust と C# の両方に同じ定数を作るためのものだった。サーバーもクライアントも C# になり、定数を書いたソースを共有すれば済むので、使わない

## 認証

- `AuthFilter` が検証した結果の `AuthContext` を `ServiceContext.Items` に入れる。ハンドラーはそこから取り出す

## 冪等性

- 保存先は `last_responses(player_id, request_id, rpc, response)`。レスポンスは MessagePack のバイト列で持つ

## DB 接続

接続は `NpgsqlDataSource` を DI に 1 つ登録して取る。シャーディング(`IShardResolver`)は持たない。
