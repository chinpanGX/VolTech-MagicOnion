# 利用ガイド

基盤を使ってゲームを作る人に向けたページ。基盤は開発中で、まだ使えない(サーバー側はできている。Unity クライアントは `ApiCore`、共通レスポンス項目の反映、Monitor まであり、そのほかの Unity 用の部品はまだ無い)。ここには、決まっている導入の形を先に書いておく。

- [導入](getting-started.md): アプリのリポジトリへの置き方、参照のしかた、使う版
- [サーバーでの使い方](server.md): `Program.cs` での登録、ハンドラーの書き方、設定
- [Unity クライアントでの使い方](client.md): `ApiCore` の組み立て、呼び方

動くものは Sandbox のサンプルで見られる。動かし方は [README](../../README.md#sandbox-のサンプル)、作りは[設計書](../designs/sandbox-gacha-date-sample.md)。サンプルの `IServerClockService` は認証なしでサーバーの時刻を変えられるので、本番のサーバーに写さない。
