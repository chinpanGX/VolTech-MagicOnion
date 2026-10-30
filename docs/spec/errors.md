# エラー

業務エラーは `FAILED_PRECONDITION` に `google.rpc.ErrorInfo` を載せて返す。この形を守れば、クライアントの `ErrorConverter` と `ReservedErrors` は無修正で使える。

| 種類 | ステータス | ErrorInfo |
| --- | --- | --- |
| 業務エラー(日付変更を含む) | `FAILED_PRECONDITION` | アプリの業務エラーはアプリの domain、基盤の予約エラーは domain `voltech` |
| サーバー内部エラー | `INTERNAL` | `voltech` / `INTERNAL_ERROR`。詳細はログにだけ出す |
| 認証エラー | `UNAUTHENTICATED` | なし |
| `x-request-id` が無い・形が違う・別の RPC で使い回した | `INVALID_ARGUMENT` | なし |

- 運び方: `google.rpc.Status` の details に `ErrorInfo` を入れ、トレーラー `grpc-status-details-bin` で返す
- `FAILED_PRECONDITION` の ErrorInfo の `metadata["action"]` に `ERROR_ACTION_STAY / RETRY / HOME / TITLE / UPDATE` を入れると、クライアントの既定の action を上書きできる。ほかのステータス(`INTERNAL` など)では上書きを読まない
- `MAINTENANCE` は `metadata["end_at"]` に終了予定(UTC の RFC 3339)を入れる(サーバーが返すのは、メンテナンスの判定を足してから)
- 知らないコードは、古いアプリが `UNKNOWN_ERROR` として扱う。reason を消しても使い回さない
- 認証エラー(`UNAUTHENTICATED`)は `INTERNAL` に変えない。変えるとクライアントの再認証が動かない

## 予約エラー

domain `voltech` のエラー。既定の action は、サーバーが `metadata["action"]` で上書きしなかったときに、クライアントが使う動き。この節では action の接頭辞 `ERROR_ACTION_` を省いて書く(C# では `ErrorAction.Retry` など)。

action ごとの動き(共通エラーハンドラーの既定の実装 `DefaultCommonErrorHandler` の場合。アプリが共通エラーハンドラーを差し替えれば変えられる):

| action | 動き |
| --- | --- |
| `STAY` | ダイアログを出し、今の画面に留まる。呼び出し側には失敗が返る |
| `RETRY` | 送り直すかタイトルに戻るかを選ぶダイアログを出す。送り直すときは同じ `x-request-id` を使う |
| `HOME` | セッションを終わらせ、ホームに戻る |
| `TITLE` | セッションを破棄し(トークンとゲーム内の日付を捨てる)、タイトルに戻る |
| `UPDATE` | ストアを開く |

| コード | 区分 | 既定の action |
| --- | --- | --- |
| `INTERNAL_ERROR` | サーバーが返す | `RETRY` |
| `DATE_CHANGED` | サーバーが返す | `TITLE` |
| `MAINTENANCE` | 定義だけ残し、今は返さない | `TITLE` |
| `UPDATE_REQUIRED` | 定義だけ残し、今は返さない | `UPDATE` |
| `ACCOUNT_SUSPENDED` | 定義だけ残し、今は返さない | `TITLE` |
| `NETWORK_ERROR` | クライアントが作る | `RETRY` |
| `UNKNOWN_ERROR` | クライアントが作る | `TITLE` |
| `CLIENT_ERROR` | クライアントが作る | `TITLE` |
| `ACCOUNT_DATA_CORRUPTED` | クライアントが作る | `STAY` |
| `ACCOUNT_SAVE_FAILED` | クライアントが作る | `STAY` |

- 予約エラーは、呼び出し側が `handledCodes`(呼び出し側が自分で処理するエラーコードの一覧。[クライアントのラッパー](../guide/client.md#ラッパー))に入れても、共通エラーハンドラーに渡す。画面ごとに扱いを変えないため
- ただし `ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED`(端末のアカウント情報の読み書きの失敗)は、共通エラーハンドラーに渡さず、`handledCodes` に関係なくいつも呼び出し側に返す。共通エラーハンドラーも画面の遷移も呼ばれない。呼び出し側に返す `ApiError.Action` は `STAY` になる
- すべての予約エラーに既定の action を決める。予約エラーを足すときは、この表と、クライアントの既定の action の一覧(`ErrorPolicyProvider` の中。[実装設計書のクライアント](../design/client.md))を一緒に直す。入れ忘れは `ErrorPolicyProviderTests` が見つける
- アプリの業務エラーは、予約エラーの domain `voltech` を使えない。クライアントは `ErrorPolicyProvider` を作るときに `ArgumentException` で拒む。サーバーは確かめない

## エラーコードの置き場所

- 予約エラーは `Abstractions` に、アプリの業務エラーはアプリの共有コードに、C# の定数として置く
- アプリのハンドラーは `BusinessException(ErrorCode, message, metadata?)` を投げるだけにする。ステータスへの変換は基盤が行う

C# での作り方は [実装設計書のサーバー](../design/server.md#エラーの変換)。
