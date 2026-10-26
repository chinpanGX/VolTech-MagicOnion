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

## ドキュメント駆動で進める

[docs/design.md](docs/design.md) を仕様書とし、コードは設計書に書かれたとおりに作る。設計書とコードが食い違ったら、正しいのは設計書。

1. 実装の前に、設計書の該当する節を読む。書かれていない振る舞い・型・公開 API は勝手に足さない
2. 設計書に無いこと、足りないこと、変えたいことがあれば、先に設計書を直す案を出し、ユーザーと合意してからコードに入る
3. 「未確定」「推奨(未確定)」「要確認」の項目は、仮の判断で実装しない。決めてから設計書に書き、そのあと実装する
4. 実装で分かったこと(API 名の違い、確かめた結果など)は設計書に戻す。チェックリストの項目は、確かめた日付と方法を添えて `[x]` にする
5. 実装が終わったら、[docs/index.md](docs/index.md) の表の「状態」を更新する
6. テストは設計書の振る舞い(エラーの形式、フィルターの順序、ヘッダーなど)を確かめるように書く

設計書の変更とコードの変更は、同じブランチで別のコミットにする(`docs:` のコミットを先に置く)。
