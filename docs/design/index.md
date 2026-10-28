# 実装設計書

[仕様書](../spec/index.md) の振る舞いを、C# と MagicOnion でどう作るかを決める。仕様書と食い違ったら、仕様書に合わせる。

- [プロジェクト構成](project-structure.md): プロジェクトとフォルダーの分け方、Unity との共有のしかた、アプリからの参照のしかた
- [サーバー](server.md): フィルター、エラーの変換、認証・トランザクションの作り方、DB 接続
- [クライアント(Unity)](client.md): VolTech-gRPC のクライアントから流用するもの・差し替えるもの

## 参考

- [MagicOnion のドキュメント(日本語)](https://cysharp.github.io/MagicOnion/ja/): フィルター、`ServiceContext`、Unity での導入など。基盤が使っている版は 7.11.0 なので、ドキュメントがそれより新しい版の内容になっていないか気をつける
- [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp): シリアライザー。Unity での導入と、IL2CPP(AOT)でのフォーマッターの生成
- [YetAnotherHttpHandler](https://github.com/Cysharp/YetAnotherHttpHandler): Unity から HTTP/2 で gRPC を呼ぶためのハンドラー
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity): Unity に NuGet のパッケージを入れる
- [Npgsql](https://www.npgsql.org/doc/): PostgreSQL への接続(`NpgsqlDataSource`)
- [DbUp](https://dbup.readthedocs.io/): 基盤のテーブルのマイグレーション
