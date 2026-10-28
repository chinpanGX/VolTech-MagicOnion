# エラー

業務エラーは `FAILED_PRECONDITION` に `google.rpc.ErrorInfo` を載せて返す。この形を守れば、クライアントの `ErrorConverter` と `ReservedErrors` は無修正で使える。

| 種類 | ステータス | ErrorInfo |
| --- | --- | --- |
| 業務エラー(日付変更を含む) | `FAILED_PRECONDITION` | アプリの業務エラーはアプリの domain、基盤の予約エラーは domain `voltech` |
| サーバー内部エラー | `INTERNAL` | `voltech` / `INTERNAL_ERROR`。詳細はログにだけ出す |
| 認証エラー | `UNAUTHENTICATED` | なし |
| `x-request-id` が無い・形が違う・別の RPC で使い回した | `INVALID_ARGUMENT` | なし |

- 運び方: `google.rpc.Status` の details に `ErrorInfo` を入れ、トレーラー `grpc-status-details-bin` で返す
- `metadata["action"]` に `ERROR_ACTION_STAY / RETRY / HOME / TITLE / UPDATE` を入れると、クライアントの既定の action を上書きできる
- `MAINTENANCE` は `metadata["end_at"]` に終了予定(UTC の RFC 3339)を入れる(サーバーが返すのは、メンテナンスの判定を足してから)
- 知らないコードは、古いアプリが `UNKNOWN_ERROR` として扱う。reason を消しても使い回さない
- 認証エラー(`UNAUTHENTICATED`)は `INTERNAL` に変えない。変えるとクライアントの再認証が動かない

## 予約エラー

domain `voltech` のエラー。

| 区分 | コード |
| --- | --- |
| サーバーが返す | `INTERNAL_ERROR`・`DATE_CHANGED` |
| 定義だけ残し、今は返さない | `MAINTENANCE`・`UPDATE_REQUIRED`・`ACCOUNT_SUSPENDED` |
| クライアントが作る | `NETWORK_ERROR`・`UNKNOWN_ERROR`・`CLIENT_ERROR`・`ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED` |

## エラーコードの置き場所

- 予約エラーは `Abstractions` に、アプリの業務エラーはアプリの共有コードに、C# の定数として置く
- アプリのハンドラーは `BusinessException(ErrorCode, message, metadata?)` を投げるだけにする。ステータスへの変換は基盤が行う

C# での作り方は [実装設計書のサーバー](../design/server.md#エラーの変換)。
