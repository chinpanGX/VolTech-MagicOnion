# 共通レスポンス項目とクライアントのヘッダー付与を、Transaction・ApiCore と一緒に見直す

*   **Author:** ちんぱん
*   **Status:** Draft
*   **Date:** 2026-11-04

--------------------------------------------------------------------------------

## Overview

[基盤の方針と進める順番](foundation-direction.md)(RFC)の「進める順番」3 番(設計書 1)。次の 2 つを、サーバーの `ITransactions.RunAsync`・`TxContext` とクライアントの `ApiCore` を直す 1 回の変更として設計する。

1. **共通レスポンス項目**(player_diff・バッジ・通知)。未マージのブランチ `feature/common-response` の実装を土台にして、RFC の Analysis 5 の指摘に答える形で直す
2. **クライアントのヘッダー付与**。`ApiCore` が呼ぶたびに `WithOptions(callOptions)` でヘッダーを付けている処理を、MagicOnion の `IClientFilter` に移す。RFC の Analysis 1 で、MagicOnion の公式ドキュメント(基準 1)から外れている大きな点として挙がった唯一の項目

3. **サーバーのハンドラーとクライアントのラッパーの生成**。共有インターフェース(`IService<T>`)から、ソースジェネレーターで両側のコードを作る。サーバーは必ず `RunAsync` を通る基底クラス、クライアントは今は手書きのラッパー。1 の「`Common` が詰められない」形を、コードで書けなくするために足す(2026-11-07 にオーナーが決定)

1 と 2 は `ApiCore` の「1 回送る」処理(`SendOnceAsync`)と「成功の後始末」(`CompleteAsync`)を書き換える。別々に設計すると、同じ箇所を 2 回直すことになる。そのため 1 本にまとめる。

クライアントとの取り決め(ステータスと ErrorInfo、ヘッダー名と値、予約エラー、冪等性の意味)は変えない(RFC の決めたこと 2)。この設計書で変えるのは、基盤の公開 API と内部の作りだけ。Uta はリリース前なので、基盤の公開 API の破壊的な変更は許す(RFC の決めたこと 3)。

この設計書が承認されたら、次の節をこの設計書へのリンクに置き換える: `docs/spec/common-response.md` の全体、`docs/design/server.md` の「共通レスポンス項目」、`docs/design/client.md` の「共通レスポンス項目の反映」とヘッダー付与の記述(具体的な節は Detailed Implementation Plan で挙げる)。

## The Problem

### 共通レスポンス項目(`feature/common-response` の今の作り)

今の作りは次のとおり。

- レスポンスの型が `IApiResponse<TCommon>` を実装する。`TCommon` はアプリが決める 1 つの型
- サーバーでは、アプリのコレクター(`ICommonResponseCollector<TCommon>`)を `AddCommonResponse<TCommon, TCollector>()` で登録する。`RunAsync` が、本体の後・レスポンスの保存の前に、同じトランザクションの中で `Common` を詰める
- クライアントでは、`IPlayerDiffApplier<TCommon>`・`IBadgeApplier<TCommon>`・`INotificationApplier<TCommon>` の 3 つを `ApiCoreBuilder` で渡す。`ApiCore` がこの順で呼ぶ

`RunAsync` の中で詰める理由は残す。保存するレスポンスに `Common` が入っていれば、同じ `x-request-id` の再送に同じ `Common` を返せる(通知が 2 回出ない)。MagicOnion の流儀(サービスフィルター)で詰めると、保存の後になってしまい、この性質が崩れる。

RFC で指摘された問題は次の 4 つ。

1. **`Common` が黙って空になる**。`Common` を詰めるのは `RunAsync` だけで、対象は `RunAsync` が返す値だけ。今の検査(`ThrowIfUnfilled`)も `RunAsync` の中にある。次の 2 つの形では、`IApiResponse<TCommon>` を実装した型でも `Common` は null のまま届く。クライアントは「変化なし」と読むので、エラーにならない
    - **`RunAsync` の外で作ったレスポンスを返す**: `RunAsync` が内部の型を返し、ハンドラーがその外でレスポンスの型に詰め替える形。内部の型は `IApiResponse<>` ではないので、詰めずに検査も通る。コレクターが記録した player_diff・通知は、コミットされたのにクライアントに届かない
    - **`RunAsync` をまったく通さない**: `TxContext` が無いのでコレクターに記録できない。読み取りの RPC なら、返すはずのバッジが届かない

    どちらもサーバーのデータは正しく、壊れるのはクライアントの表示。キャッシュはサーバーとずれたまま進み、次のサインインで全件を取り直すまで直らない。通知は取り直せないので失われる
2. **保存済みのレスポンスとの互換**。`last_responses` には、MessagePack のバイト列が残る。デプロイで `TCommon`(またはレスポンスの型)の形が変わったあと、デプロイ前に保存されたものを同じ `x-request-id` で再送されると、新しい型として読めないおそれがある。読めなければ再送が `INTERNAL` になる。読めても、値がずれて入るおそれがある。これは `TCommon` に限らず、保存するレスポンスすべての問題。ただし `TCommon` は全レスポンスに共通なので、1 回の変更で全 RPC に影響する
3. **IL2CPP(AOT)への依存**。クライアントは、レスポンスを `object` で受け取り、`is IApiResponse<TCommon>` で判定してから `Common` を取り出す(`CommonApplier`)。MessagePack のフォーマッターは、具体的なレスポンスの型ごとに生成される。そのため、ジェネリックのインターフェースそのもののフォーマッターは要らないはず(推測。確かめていない)。どこまでが AOT で動く前提なのかが、まだ書かれていない
4. **部品が多い**。反映処理のインターフェースが 3 つ、`ApiCoreBuilder` のメソッドが 3 つ、内部の `CommonApplier`、`Build` での `TCommon` の食い違いの検査がある。基盤は `TCommon` の中身を知らない。それなのに、player_diff → バッジ → 通知という中身の区別と順序を、基盤が持っている。`TCommon` を受け取る 1 つの関数にまとめれば、食い違いの検査も要らなくなる。ただし、VolTech-gRPC 版と名前がそろわなくなる

### クライアントのヘッダー付与

`ApiCore.SendOnceAsync` は、呼ぶたびに `CallOptions` を作り、`client.WithOptions(callOptions)` で渡している。中身は、ヘッダー(`authorization`・`x-request-id`・`x-game-date`・アプリのバージョンなど)、締め切り、セッションのキャンセルの 3 つ。

MagicOnion の公式ドキュメントでは、呼び出しに共通するヘッダーは `IClientFilter` で付ける。RFC の Analysis 7 では、Monitor(設計書 4)も `IClientFilter` で全通信を記録する前提になっている。ヘッダーを付ける場所がフィルターの外に残ると、Monitor が見るリクエストと、実際に送ったヘッダーとがずれる。

ただし、フィルターだけでは移しきれない。2026-11-04 に、MagicOnion.Client 7.11.0 を .NET 10 の動的クライアントで動かして確かめた(スクラッチのプログラム。リポジトリには残していない)。

- フィルターの中で `context.CallOptions.Headers` は null ではなく、足したヘッダーは送信に載る。呼び出しごとに新しい `Metadata` になる(前の呼び出しのヘッダーは残らない)
- `MagicOnionClient.Create<T>(callInvoker, filters)` で渡したフィルターは、`WithOptions` の後も動く。順番は配列の先頭が外側
- `RequestContext.CallOptions` は読み取り専用(`CallOptions` は struct で、置き換えられない)。**締め切りとキャンセルは、フィルターからは変えられない**。`WithOptions` で渡す必要が残る
- `RequestContext.Items` は呼び出しごとに MagicOnion が空で作る。呼ぶ側から値を入れておく口は無い

そのため、再送で同じ値を使う `x-request-id` を、`ApiCore` の再送のループからフィルターへどう渡すかを決める必要がある。

## Goals

*   `IApiResponse<TCommon>` を実装したレスポンスで `Common` が詰められない形を、コードで書けなくする。アプリは生成された基底クラスの `TxContext` を受け取るメソッドを実装し、`RunAsync` を手で呼ばない
*   共有インターフェースから、サーバーの基底クラスとクライアントのラッパーを同じジェネレーターで生成する。`[SkipAuth]`・`[NoSideEffects]` は生成時に読み、クライアントの実行時のリフレクションをやめる
*   保存済みのレスポンスを今の型として読めないときの、再送の振る舞いを決め、仕様に書く
*   クライアントの共通レスポンス項目の反映の公開 API を、基盤が `TCommon` の中身を知らない前提に合う最小の形にする
*   クライアントが付けるヘッダーを `IClientFilter` に移す。ヘッダーの名前・値・付く条件、再送で同じ `x-request-id` を使うことは変えない
*   AOT(IL2CPP)で動く前提(使ってよいリフレクション・ジェネリック)を書き、確かめる方法を決める
*   変えた振る舞いを、サーバー(`tests/VolTech.Server.Tests`)と EditMode(`Tests/EditMode`)のテストで確かめる

## Non-Goals

*   `TCommon` の中身(player_diff・バッジ・通知の型)を基盤で決めること。アプリが決める
*   クライアントとの取り決めを変えること(ヘッダー名、ErrorInfo、予約エラー、冪等性の意味)
*   反映処理の登録忘れを、起動時に調べる仕組み(RFC の Non-Goals。VolTech-gRPC 版にも無い)
*   Monitor・Tracing(設計書 4)。ただし、ここで作る `IClientFilter` を Monitor が使える置き方にする
*   ログと計測(設計書 2)。クライアントのログ(`IApiLogger`)の出し方は変えない
*   `last_responses` の保存形式(MessagePack のバイト列)を変えること
*   再送・再認証・エラーハンドラーの呼び出しを `IClientFilter` に移すこと。順番待ち・セッションの世代・画面の遷移と絡むので、`ApiCore` に残す
*   StreamingHub

## Open Questions

Technical Plan の節で、選択肢を挙げてオーナーが決める。

*   **Q1(解決)**: `Common` が詰められないレスポンスは、実行時に検査せず、ハンドラーをコード生成で包んで書けなくする。生成はクライアントのラッパーも含む両側(2026-11-07)
*   **Q2**: 保存済みのレスポンスを今の型として読めないとき、再送をどうするか(`INTERNAL` で失敗させる、保存の行にスキーマの版を持たせて合わなければ失敗させる、など)
*   **Q3**: クライアントの反映処理を、`TCommon` を受け取る 1 つにするか、VolTech-gRPC 版と同じ名前の 3 つのままにするか
*   **Q4**: 再送で同じ値を使う `x-request-id` を、`ApiCore` から `IClientFilter` へどう渡すか
*   **Q5**: アプリが自分の `IClientFilter` を足せるようにするか。足せるなら、基盤のフィルターとの順序をどう決めるか
*   **Q6**: ジェネレーターの対象をどう決めるか。DB を使わない RPC(トランザクションを開かない RPC)をどう表すか
*   **Q7**: ソースジェネレーターが Unity 7(C# 9、IL2CPP)の手元のパッケージ構成で動くか。未確認

--------------------------------------------------------------------------------

*Drafted with EGM*
