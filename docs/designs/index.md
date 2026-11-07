# 設計書(egm)

egm のプラグイン(`/egm:egm-*`)で書いた設計書を置く。機能ごとに、振る舞い(仕様)と作り方(実装設計)を 1 つの文書に書く。置き場所・書式は egm の標準のままにする。手順は [CLAUDE.md](../../CLAUDE.md#egm-で設計書を書く)。

承認した設計書は、[仕様書](../spec/index.md)・[実装設計書](implementation/index.md)より優先する。同じことを決めた仕様書・実装設計書の節は、ここの設計書へのリンクに置き換える。

## 承認済み

- [共通レスポンス項目とクライアントのヘッダー付与](common-response-and-client-filter.md): 共通レスポンス項目と `IClientFilter` への移行、サーバーのハンドラーとクライアントのラッパーの生成を、Transaction・`ApiCore` と一緒に見直す。2026-11-07 に承認。実装の前に A1〜A6 を確かめる

## 検討中

- [基盤の方針と進める順番](foundation-direction.md): gRPC 版から 4 つを除いた機能をそろえる方針、判断の規則(取り決めは変えない、迷ったら MagicOnion のベストプラクティス)、設計書を書く順番。egm のブレストの結果(RFC)

## 実装設計書

[implementation/](implementation/index.md) は egm の設計書ではない。`docs/designs/` の設計書がまだ扱っていない部分の、仕様を C# と MagicOnion でどう作るかを書く。承認済みの設計書より優先しない。
