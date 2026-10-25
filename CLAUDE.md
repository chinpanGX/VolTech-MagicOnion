# VolTech-MagicOnion

MagicOnion でゲームのバックエンドを作る基盤(C# サーバー + Unity クライアント)。入口は [README.md](README.md)、計画は [docs/index.md](docs/index.md)。

## コードのスタイル

- C# は Allman スタイル。`var` を常に使う(組み込み型も含む)。ルールは `.editorconfig` にある
- private / internal のフィールドは接頭辞なしの camelCase(`_` を付けない)。public / protected は PascalCase
- 改行コードは LF
- `VolTech.Server.Abstractions` は Unity と共有するので、netstandard2.1 / C# 9 で書ける範囲にする(`record`・`init` は使わない)

## 他のリポジトリとの関係

- VolTech-gRPC(proto + Rust)とは、通信の取り決め(エラー形式・ヘッダー名)を同じにする。`ApiHeaders` の値を変えるときは、クライアントの `ApiHeaders` と一緒に直す
- MasterDataTools は別のリポジトリ。このリポジトリは参照しない

## ドキュメント

設計・実装・ドキュメントは、このリポジトリの中で行き来しながら更新する。コードを変えたら、`docs/` の該当ページが合っているか確かめる。
