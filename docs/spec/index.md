# 仕様書

基盤が何をするか(振る舞いと、クライアントとの取り決め)を決める。MagicOnion や C# でどう作るかは [実装設計書](../design/index.md) に書く。

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

強制アップデートは、リリース済みのアプリが `UPDATE_REQUIRED` を受け取って更新を促せないと、後からサーバーに足しても古いアプリには効かない。クライアントが受け口を持ち、バージョンを送ってさえいれば、サーバーの判定は必要になった時点で 1 段足すだけで済む。メンテナンスも同じ。

仕様の根拠は、VolTech-gRPC の `docs/wiki` と `server/rust`、クライアントは `Unity/com.chinpangx.voltech.grpc`(v0.6.0)のソース。VolTech-gRPC のタグから分岐はせず、別の製品として版は 0.1.0 から始める。

## ページ

- [リクエストの処理順](processing-order.md): 1 つの RPC を受けてから返すまでに、基盤が行う処理とその順序
- [エラー](errors.md): ステータスと ErrorInfo の形、予約エラー
- [ヘッダーと RPC ごとの設定](headers.md): ヘッダー、`[SkipAuth]`・`[NoSideEffects]`、共通レスポンス項目
- [認証](auth.md): 端末の登録、トークン、プレイヤーの状態
- [冪等性・トランザクション・ゲームログ](transaction.md)
