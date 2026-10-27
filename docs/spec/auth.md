# 認証

Rust 版の `auth` クレートと同じ振る舞いを C# で再現する。

- `DeviceService`(Register / IssueToken)を提供する。認証は不要(`[SkipAuth]`)
- `authorization: Bearer` のトークンを検証し、通ったリクエストでは、ハンドラーが `AuthContext`(device\_id・player\_id・player\_created)を取り出せる
- 無効・期限切れは `UNAUTHENTICATED`。クライアントはトークンを取り直し、1 回だけ自動で再送する
- プレイヤーの状態は `IPlayerResolver`(NotCreated / Active)をアプリが実装する。アカウント停止(Suspended と `ACCOUNT_SUSPENDED`)は持たない
- トークンの有効期限は既定 1 時間(Rust 版と同じ)
