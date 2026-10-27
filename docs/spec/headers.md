# ヘッダーと RPC ごとの設定

## ヘッダー

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

## RPC ごとの設定は属性に置く

Rust 版は proto の `idempotency_level` と `skip_auth` で RPC ごとの扱いを決めていた。C# では共有インターフェースの属性で持つ。

| 属性 | 意味 |
| --- | --- |
| `[SkipAuth]` | 認証を通さない |
| `[NoSideEffects]` | 副作用がない。冪等性の対象から外し、読み取り専用のトランザクションで動かす |

`[NoSideEffects]` の付かない RPC は、すべて冪等性の対象にする。サーバーとクライアント(`RpcSettings`)が同じ属性を読むので、食い違わない。

## 共通レスポンス項目

`player_diff`・バッジ・通知は、`IApiResponse` を実装した MessagePack の型で返す。
