# 認証

Rust 版の `auth` クレートと同じ振る舞いを C# で再現する。

- `DeviceService`(Register / IssueToken)を提供する。認証は不要(`[SkipAuth]`)
- `authorization: Bearer` のトークンを検証し、通ったリクエストでは、ハンドラーが `AuthContext`(device\_id・player\_id・player\_created)を取り出せる
- 無効・期限切れは `UNAUTHENTICATED`。認証が要る RPC なら、クライアントは弾かれたトークンを捨てて取り直し、1 回だけ自動で再送する。再送には同じ `x-request-id` を使う
- 再送しても `UNAUTHENTICATED` なら、それ以上は自動で再送しない。クライアントは弾かれたトークンを捨て、`UNKNOWN_ERROR` として共通エラーハンドラーに渡す(既定の action は [予約エラーの表](errors.md#予約エラー))。共通エラーハンドラーが送り直し(`ErrorDecision.Retry`)を選んだら、新しいトークンで同じ `x-request-id` のまま送り、また `UNAUTHENTICATED` なら 1 回だけ自動で再送する
- `[SkipAuth]` の RPC が `UNAUTHENTICATED` を受けたら(端末の認証情報が無効なときの `IssueTokenAsync` や、サーバーとクライアントで設定が食い違ったとき)、トークンを付けていないので取り直さず、すぐに `UNKNOWN_ERROR` として共通エラーハンドラーに渡す
- トークンを取るとき(`ITokenProvider.GetTokenAsync`)に `UNAUTHENTICATED` を受けたら、捨てるトークンが無いので何も捨てず、`UNKNOWN_ERROR` として共通エラーハンドラーに渡す。セッションを終わらせた後に届いた古い呼び出しの結果では、トークンを捨てない
- 共通エラーハンドラーは、`ApiCore` を作るときにアプリが渡す、エラーを一か所で処理する部品(`ICommonErrorHandler`。既定の実装は `DefaultCommonErrorHandler`)
- 認証の失敗に専用の予約エラーは作らない。アプリが見分けたいときは `ApiError.StatusCode`(`Unauthenticated` のまま残る)を見る
- プレイヤーの状態は `IPlayerResolver`(NotCreated / Active)をアプリが実装する。アカウント停止(Suspended と `ACCOUNT_SUSPENDED`)は持たない
- トークンの有効期限は既定 1 時間(Rust 版と同じ)。設定で変えられる
- トークンはデバイスごとに 1 つだけ持つ。発行し直すと、古いトークンは無効になる
