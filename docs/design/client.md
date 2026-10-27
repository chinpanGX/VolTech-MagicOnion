# クライアント(Unity)

VolTech-gRPC のクライアント(`com.chinpangx.voltech.grpc` v0.6.0)から作る。通信の骨格は流用でき、変わるのは `call` の型、レスポンス型の扱い、`DeviceTokenProvider` の通信部分の 3 点。`ApiCoreBuilder` の公開 API は残し、中身だけを差し替えれば、アプリ側のコードは書き直さずに済む。

| 対象 | 扱い | 理由 |
| --- | --- | --- |
| `ApiChannel`(YetAnotherHttpHandler + `GrpcChannel`) | 流用 | `MagicOnionClient.Create<T>(callInvoker)` に同じ CallInvoker を渡せる |
| `ApiCore` の制御(順番待ち、再認証、セッション世代、進行中通知、エラーハンドラー) | 流用 | 通信手段に依存しない |
| `ErrorConverter`、`ErrorPolicy`、`ReservedErrors`、`ErrorHandling/` | 流用 | サーバーが ErrorInfo を同じ形で返す限り無修正 |
| `EncryptedFileAccountStore` などアカウント保存 | 流用 | `StoredAccount` は protobuf のまま残す案が最小(未確定) |
| `ApiHeaders`、`AppVersion`、`ServerClock` | 流用 | ヘッダー名が同じ |
| `ApiCore.InvokeAsync` の `call`(`AsyncUnaryCall<T>`) | 差し替え | `UnaryResult<T>` に変える。`WithOptions(CallOptions)` でヘッダーと締め切りを渡す |
| `IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier` | 差し替え | `IMessage` を `IApiResponse` に変える |
| `DeviceTokenProvider` の通信部分 | 差し替え | 生成された `DeviceGrpc` を MagicOnion の `IDeviceService` に替える |
| `ApiServiceBase` と生成ラッパー、`RpcSettings` | 差し替え | 共有インターフェースの属性から設定を読む |
| Tracing(gRPC Monitor) | 要確認 | protobuf メッセージの記録が前提だと思われる(未読) |
| `protoc-gen-voltech-grpc`(ラッパー生成)、buf の設定、`Runtime/Generated/` の RPC 部分 | 不要 | proto を使わなくなる。エラーコードも C# の定数を共有するので生成しない |

`Google.Protobuf` への依存は残す。エラー詳細(`google.rpc.Status`)の解析とアカウント保存で使っているため。
