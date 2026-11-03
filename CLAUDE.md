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

`docs/` は読む人で 4 つに分かれる。

| フォルダー | 中身 |
|---|---|
| [docs/designs/](docs/designs/index.md) | egm の設計書。機能ごとに、振る舞いと作り方を 1 つの文書に書く。新しい機能・変更はここに書く |
| [docs/spec/](docs/spec/index.md) | 仕様書。基盤が何をするか(振る舞いと、クライアントとの取り決め) |
| [docs/design/](docs/design/index.md) | 実装設計書。仕様を C# と MagicOnion でどう作るか |
| [docs/guide/](docs/guide/index.md) | 利用ガイド。基盤を使う人に向けた導入と使い方 |

コードは設計書に書かれたとおりに作る。食い違ったら、`docs/designs/` の承認済みの設計書 → 仕様書 → 実装設計書 → コードの順に正しい。`docs/spec/`・`docs/design/` は、`docs/designs/` の設計書がまだ扱っていない部分の正として残す。`docs/designs/` の設計書が同じことを決めたら、`docs/spec/`・`docs/design/` の該当する節はその設計書へのリンクに置き換える。

### egm で設計書を書く

設計書は egm のプラグイン(`/egm:egm-*`)で書く。置き場所・書式は egm の標準のままにする。

1. `/egm:egm-brainstorm` で要件と選択肢を詰め、`concepts_brief.md` にまとめる(省いてよい)
2. `/egm:egm-draft` で `docs/designs/` に設計書を書く
3. `/egm:egm-review <設計書のパス>` でレビューし、`/egm:egm-resolve` で指摘への対応を決める
4. ユーザーが承認したら、設計書を正とする。[docs/designs/index.md](docs/designs/index.md) に載せ、`docs/spec/`・`docs/design/` の重なる節をリンクに置き換える
5. 実装の後は `/egm:egm-drift-report` で設計書とコードのずれを確かめ、ずれは設計書に戻す

### 実装の手順

1. 実装の前に、`docs/designs/` の設計書と、仕様書・実装設計書の該当する節を読む。書かれていない振る舞い・型・公開 API は勝手に足さない
2. 書かれていないこと、足りないこと、変えたいことがあれば、先に `docs/designs/` の設計書を書く・直す案を出し、ユーザーと合意してからコードに入る
3. 「未確定」「推奨(未確定)」「要確認」の項目は、仮の判断で実装しない。決めてから書き、そのあと実装する
4. 実装で分かったこと(API 名の違い、確かめた結果など)は、その部分の正の設計書(`docs/designs/` か実装設計書)に戻す。[docs/index.md](docs/index.md) のチェックリストの項目は、確かめた日付と方法を添えて `[x]` にする
5. 実装が終わったら、[docs/index.md](docs/index.md) の表の「状態」を更新する。アプリから見た使い方が変わったら、利用ガイドも直す
6. テストは仕様書の振る舞い(エラーの形式、処理の順序、ヘッダーなど)を確かめるように書く

ドキュメントの変更とコードの変更は、同じブランチで別のコミットにする(`docs:` のコミットを先に置く)。
