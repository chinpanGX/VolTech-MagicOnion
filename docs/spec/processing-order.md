# リクエストの処理順

基盤は、1 つの RPC に対して次の処理を外側から順に行う。順序は Rust 版の Layer と同じにする。順序を変えると、クライアントとの取り決め(再認証・エラー変換)が崩れるので、アプリは順序を入れ替えられない。

1. アクセスログと例外の変換(Rust の `RpcLogLayer`): RPC ごとの span とアクセスログを残す。内側で起きた例外を、[エラー](errors.md) の形に一元変換する
2. サーバー時刻(`ServerStatusLayer` のうちヘッダーの部分): レスポンスに `x-server-time` と `x-game-date` を付ける。業務エラーのレスポンスにも付ける。DB は使わない。強制アップデートとメンテナンスの判定は持たない(足すときはこの段に入れる)
3. 認証(`AuthLayer`): Bearer を検証する。`[SkipAuth]` の RPC は検証を省き、そのまま次へ進める。詳細は [認証](auth.md)
4. 日付変更の判定(`DateChangeLayer`): リクエストの `x-game-date` が今のゲーム内の日付と違えば `DATE_CHANGED` を返す。認証の内側に置く
5. 本体: トランザクションの中で動かす。冪等性の判定もここで行う。詳細は [冪等性・トランザクション・ゲームログ](transaction.md)

- 対象は Unary のみ。StreamingHub は別に設計する
- サーバーは HTTP/2 で待ち受ける。クライアント(YetAnotherHttpHandler)は HTTP/2 で接続する

MagicOnion での作り方は [実装設計書のサーバー](../design/server.md#フィルター)。
