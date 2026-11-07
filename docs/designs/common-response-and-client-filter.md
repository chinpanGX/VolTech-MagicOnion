# 共通レスポンス項目・クライアントのヘッダー付与・RPC のコード生成を、Transaction・ApiCore と一緒に見直す

*   **Author:** ちんぱん
*   **Status:** Approved
*   **Date:** 2026-11-07

--------------------------------------------------------------------------------

## Overview

[基盤の方針と進める順番](foundation-direction.md)(RFC)の「進める順番」3 番(設計書 1)。次の 3 つを 1 回の変更として設計する。サーバーは、ハンドラーが `ITransactions.RunAsync` を通る形を生成で固定する(`RunAsync`・`TxContext` の動きは変えない)。クライアントは `ApiCore` とラッパーの作りを変える。

1. **共通レスポンス項目**(player_diff・バッジ・通知)。`main` に入っている実装(`feature/common-response` で入れたもの)を、RFC の Analysis 5 の指摘に答える形で直す
2. **クライアントのヘッダー付与**。`ApiCore` が呼ぶたびに `WithOptions(callOptions)` でヘッダーを付けている処理を、MagicOnion の `IClientFilter` に移す。RFC の Analysis 1 で、MagicOnion の公式ドキュメント(基準 1)から外れている大きな点として挙がった唯一の項目
3. **サーバーのハンドラーとクライアントのラッパーの生成**。共有インターフェース(`IService<T>`)から、ソースジェネレーターで両側のコードを作る。サーバーは必ず `RunAsync` を通る基底クラス、クライアントは今は手書きのラッパー。1 の「`Common` が黙って空になる」問題を、コードの形で防ぐために足す(2026-11-07 にオーナーが決定)

1 本にまとめる理由は、3 つとも基盤の公開 API の組み立て方を変えるから。1 は `ApiCoreBuilder`、2 は `ApiCore.CreateBuilder` と `DeviceTokenProvider` の引数、3 は `ApiServiceBase` とラッパーの形を変える。別々に出すと、Uta が組み立ての処理を何度も移すことになる。コードの上では、変える箇所はほとんど重ならないので、実装は段階ごとにコミットできる(Detailed Implementation Plan の「実装の順番」)。

RFC の「進める順番」の 1・2 番(YetAnotherHttpHandler でトレーラーが届くかの確認、取り決めの固定)は、この設計の前提にしない。この設計はエラーの経路とクライアントとの取り決めを変えないので、結果に左右されない。

クライアントとの取り決め(ステータスと ErrorInfo、ヘッダー名と値、予約エラー、冪等性の意味)は変えない(RFC の「まだ決めていないこと」の 2。解決済み)。この設計書で変えるのは、基盤の公開 API と内部の作りだけ。Uta(この基盤を使う個人開発のゲーム)はリリース前なので、基盤の公開 API の破壊的な変更は許す(同じく 3)。

この設計書が承認されたら、実装の節をこの設計書へのリンクに置き換える: `docs/designs/implementation/client.md` の「共通レスポンス項目の反映」・「ラッパーの書き方」とヘッダー付与の記述。`docs/designs/implementation/server.md` の「共通レスポンス項目」は、コレクターの説明をこの設計書が変えないので残し、リンクを足す。振る舞いとクライアントとの取り決めは `docs/spec/` に残し、`common-response.md`・`headers.md`・`transaction.md` の変わる節を直す(具体的な節は Detailed Implementation Plan で挙げる)。CLAUDE.md の規則では重なる節をリンクに置き換えるが、クライアントとの取り決めは `docs/spec/` だけを読めば分かるようにしたいので、ここは書き直して残す。食い違ったら、CLAUDE.md の順のとおり設計書が正しい。

## The Problem

### 共通レスポンス項目(今の作り)

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

MagicOnion の公式ドキュメントでは、呼び出しに共通するヘッダーは `IClientFilter` で付ける。RFC の Analysis 7 では、Monitor(設計書 4)も `IClientFilter` で全通信を記録する前提になっている。ただし、`WithOptions` のヘッダーもフィルターから読めるので、Monitor のために移す必要はない。移す理由は、公式の書き方に合わせること(2026-11-07 にオーナーが決定)。

ただし、フィルターだけでは移しきれない。2026-11-04 に、MagicOnion.Client 7.11.0 を .NET 10 の動的クライアントで動かして確かめた(スクラッチのプログラム。リポジトリには残していない)。Unity のソースジェネレーターのクライアントでも同じかは未確認。

- フィルターの中で `context.CallOptions.Headers` は null ではなく、足したヘッダーは送信に載る。呼び出しごとに新しい `Metadata` になる(前の呼び出しのヘッダーは残らない)
- `MagicOnionClient.Create<T>(callInvoker, filters)` で渡したフィルターは、`WithOptions` の後も動く。順番は配列の先頭が外側
- `RequestContext.CallOptions` は読み取り専用(`CallOptions` は struct で、置き換えられない)。**締め切りとキャンセルは、フィルターからは変えられない**。`WithOptions` で渡す必要が残る
- `RequestContext.Items` は呼び出しごとに MagicOnion が空で作る。呼ぶ側から値を入れておく口は無い
- `WithOptions` で渡したヘッダーは、フィルターの `context.CallOptions.Headers` で読める
- 先頭のフィルターの `SendAsync` は、RPC のメソッドを呼んだ時点で、同じスレッドで同期的に始まる(`await` の前に入る)。`AsyncLocal` に入れた値は、待機をまたがずに読める(2026-11-07、同じ方法で確かめた。IL2CPP でも同じに動く前提で設計する。実機の確認は Technical Plan の 5 節)

そのため、呼び出しごとの値を `ApiCore` からフィルターへどう渡すかを決める必要がある。`x-request-id` は再送で同じ値を使う。トークンの取得はフィルターに移せない。今は `GetTokenAsync` の後に締め切りを計算しているので、フィルターで待つと、順番待ち・端末登録・発行の時間が RPC の締め切りを消費する(締め切りはフィルターから変えられない)。

### RPC のコード生成

ラッパーも基底クラスも生成しない今の作りには、次の問題がある。

- **サーバー**: 上の問題 1 は、ハンドラーが手で `RunAsync` を呼ぶ作りから来ている
- **クライアント**: 今の `ApiServiceBase` は `[SkipAuth]`・`[NoSideEffects]` を実行時のリフレクションで読んでおり、IL2CPP で読めるかが未確認(`docs/index.md`)。読めないと、認証や冪等性の設定が黙って既定の値になる。生成時に読めば、この確認は要らなくなる

クライアントも生成する理由は、もう 1 つある。オーナーは、サーバーとクライアントの作り方をそろえ、同じ共有インターフェースから両側を作ることを選んだ(2026-11-07)。VolTech-gRPC 版でも、クライアントのラッパーは生成している。

`docs/designs/implementation/client.md` は「今の規模では手間に見合わない」として生成を見送っていた。サーバーの生成で共有インターフェースを読む部分を作ることになったので、クライアントはその部分を共有できる。増えるのは、クライアントのコードの出力、Unity への DLL の配布、DLL の検査(`CommittedDllTests`)の 3 つ。これを受け入れて、この判断を置き換える。

## Goals

*   生成された基底クラスを使う限り、`IApiResponse<TCommon>` を実装したレスポンスが `RunAsync` を通らずに返る書き方をできなくする。アプリは `TxContext` を受け取るメソッドを実装し、`RunAsync` を手で呼ばない。`ServiceBase<T>` の直接継承はアプリのテストで止める
*   共有インターフェースから、サーバーの基底クラスとクライアントのラッパーを同じジェネレーターで生成する。`[SkipAuth]`・`[NoSideEffects]` は生成時に読み、クライアントの実行時のリフレクションをやめる
*   保存済みのレスポンスを今の型として読めないときの、再送の振る舞いを決め、仕様に書く
*   クライアントの共通レスポンス項目の反映の公開 API を、基盤が `TCommon` の中身を知らない前提に合う最小の形にする
*   クライアントが付けるヘッダーを `IClientFilter` に移す。ヘッダーの名前・値・付く条件、再送で同じ `x-request-id` を使うことは変えない
*   AOT(IL2CPP)で動く前提(使ってよいリフレクション・ジェネリック)を書き、確かめる方法を決める
*   変えた振る舞いを、サーバー(`tests/VolTech.Server.Tests`)と EditMode(`Tests/EditMode`)のテストで確かめる。ジェネレーターは、生成結果とコンパイルエラーをテストする

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

未解決の項目は、実装の最初に確かめる次の 6 つと、iOS の確認だけ。結果によって、下の Q3・Q4・Q7、CUJ 1、Technical Plan の 1・2 節、Non-Goals が変わることがある。内容と影響は「最初に確かめる項目」の表に書く。

*   **A1**: ソースジェネレーターのクライアントでの同期開始。`[MagicOnionClientGeneration]` を書き忘れたときの失敗の仕方
*   **A2**: アプリのメソッドのフィルター属性が効くか
*   **A3**: 本物の `MagicOnionClient` を EditMode で動かせるか
*   **A4**: パッケージの中の DLL が Unity のコードに効くか
*   **A5**: IL2CPP での `IApiResponse<TCommon>` の判定とフォーマッター
*   **A6**: Unity Test Framework のテストを IL2CPP のプレイヤーで動かせるか
*   **iOS の AOT**: Mac が要るので、この設計書の完了条件に入れない(Verification Plan)

### 解決済み

番号は Alternatives Considered から参照するので残す。

*   **Q1(解決)**: `Common` が黙って空になる問題は、実行時に検査せず、ハンドラーをコード生成で包んで書けなくする。生成はクライアントのラッパーも含む両側(2026-11-07)
*   **Q2(解決)**: 保存済みのレスポンスを今の型として読めないときは、例外のまま `INTERNAL` にする。コードは足さず、動きを仕様に書く。処理はコミット済みなので、再送は成功させられない。タイトルに戻れば、サインインで全件を取り直せる。その応答の通知は失われる。値がずれて読めた場合は検出しない(2026-11-07)
*   **Q3(解決)**: クライアントの反映処理は、`TCommon` を受け取る 1 つにまとめる。player_diff → バッジ → 通知の順は、アプリが守る取り決めにする。VolTech-gRPC 版と名前はそろわなくなる(2026-11-07)
*   **Q4(解決)**: ヘッダーは全部 `IClientFilter` で付け、公式の書き方に合わせる。トークンの取得は `ApiCore` に残し、取得した値と `x-request-id` を `AsyncLocal` の入れ物でフィルターへ渡す。締め切りとキャンセルは `WithOptions` に残る(2026-11-07。レビューの指摘を受けて、取得まで移す形から直した)
*   **Q5(解決)**: アプリは `IClientFilter` を足せない。フィルターは基盤のものだけで、基盤のヘッダーのフィルターが必ず先頭になる。Monitor(設計書 4)も基盤の内部で足す。必要になったら、基盤より内側に足す口を作る(2026-11-07)
*   **Q6(解決)**: ジェネレーターの対象は、アプリが `partial class` に `typeof(共有インターフェース)` を書いた属性で明示する。MagicOnion の `[MagicOnionClientGeneration]` と同じ形(2026-11-07)
*   **Q6b(解決)**: 生成する基底クラスは、全 RPC で `RunAsync` を通す。外す属性は足さない。DB を使わない RPC が必要になったら、そのサービスだけ `ServiceBase<T>` を手書きにする(2026-11-07)
*   **Q7(解決)**: ソースジェネレーターは Unity 7 で動く。Uta の `client/uta` では、MagicOnion・MessagePack・MasterMemory のジェネレーターが `RoslynAnalyzer` ラベルの DLL として動いている。基盤も同じラベルの DLL で配る。パッケージの中に置く形は、実装の最初に A4 で確かめる(2026-11-07)

## Critical User Journeys (CUJs)

### CUJ 1: サーバーの RPC を足す

*   **Given**: アプリが共有インターフェース `IGachaService` を持ち、`IApiResponse<AppCommon>` のレスポンス型を返す
*   **When**: `[GenerateVoltechService(typeof(IGachaService))] public abstract partial class GachaServiceBase { }` を書き、`GachaService : GachaServiceBase` で `DrawAsync(TxContext tx, DrawRequest request)` を実装する
*   **Then**: `RunAsync` を手で呼ばなくても、全 RPC が `RunAsync` を通る。実装しなければコンパイルが通らない。この基底クラスを使う限り、外で詰め替える書き方はできない。`AddCommonResponse` を登録し忘れたら、`RunAsync` の検査で `INTERNAL` になる

### CUJ 2: クライアントから RPC を呼び、反映する

*   **Given**: アプリが `ApiCoreBuilder` に反映処理(`ICommonResponseApplier<AppCommon>`)を 1 つ渡している
*   **When**: 生成された `GachaApi.DrawAsync(request)` を呼ぶ
*   **Then**: 基盤のフィルターがヘッダーを付けて送る。成功したら、呼び出し側に返る前に反映が終わっている

### CUJ 3: トークンが切れていて、再認証する

*   **Given**: 持っているトークンがサーバーで無効になっている
*   **When**: 認証が要る RPC を呼ぶ
*   **Then**: サーバーが `UNAUTHENTICATED` を返す。`ApiCore` は使ったトークンを `Invalidate` に渡し、同じ `x-request-id` で 1 回だけ送り直す。動きは今と同じ

### CUJ 4: デプロイをまたいで再送する

*   **Given**: 保存済みのレスポンスを、新しい型として読めない
*   **When**: クライアントが同じ `x-request-id` で再送する
*   **Then**: サーバーは `INTERNAL` を返す。処理はコミット済みなので再実行しない。`INTERNAL_ERROR` の既定は Retry だが、送り直しても同じ `x-request-id` で必ず失敗する。タイトルに戻ってサインインすれば全件を取り直せる。その応答の通知は失われる

## Technical Plan (Architecture)

### 全体の流れ

```text
共有インターフェース(IGachaService)
  │  VolTech.Generators(ソースジェネレーター)
  ├─ サーバー: GachaServiceBase ── RunAsync を必ず通す
  └─ クライアント: GachaApi ─────── ApiCore を必ず通す

クライアント                                  サーバー
GachaApi.DrawAsync
  → ApiCore(順番待ち・再送のループ)
    → トークンを取る → 締め切りを計算する
    → ApiCallContext を AsyncLocal に入れる
    → client.WithOptions(締め切り・キャンセル)
      → ApiHeaderFilter(先頭。ヘッダーを付ける) → RPC
                                                 フィルター(ログ → 時刻 → 認証 → 日付)
                                                 GachaServiceBase.DrawAsync
                                                   → ITransactions.RunAsync
                                                     → 本体 → Common を詰める → 保存 → コミット
    ← レスポンス
    → 世代の確認 → レスポンスヘッダーの反映 → ICommonResponseApplier
  ← ApiResult
```

### 1. ソースジェネレーター

*   **置き場所**: 新しいプロジェクト `src/VolTech.Generators`(netstandard2.0。Roslyn 4.3 に合わせる。MagicOnion のジェネレーターと同じ)。サーバー用とクライアント用の 2 つのジェネレーターを 1 つの DLL に入れる
*   **配り方**: サーバーは `ProjectReference` に `OutputItemType="Analyzer"` を付けて使う。Unity は、ビルドした DLL を `Unity/com.chinpangx.voltech.magiconion/Analyzers/` に置いてコミットする。`.meta` に `RoslynAnalyzer` ラベルを付け、全プラットフォームを無効にする。DLL は Release で `-p:UpdateUnityAnalyzer=true` を付けたビルドのときだけ、ビルド後の処理で写す。手では写さない
*   **対象の指定**: `partial class` に属性を付ける。属性は参照先のアセンブリの型も `typeof` で指定できる
*   **エラー**: 次はコンパイルエラーにする。`UnaryResult<T>` 以外を返すメソッド(戻り値なしの `UnaryResult` を含む)、`partial` でないクラス(コンパイラーの CS0260 で止まる)、`IService<T>` でない型(StreamingHub を含む)。一覧は Detailed Implementation Plan の `VTG001`〜`VTG005`

#### サーバーの生成物

`[GenerateVoltechService(typeof(IGachaService))] public abstract partial class GachaServiceBase { }` から、次を生成する(形の例で、実コードではない)。

```text
abstract partial class GachaServiceBase : ServiceBase<IGachaService>, IGachaService
  public UnaryResult<DrawResponse> DrawAsync(DrawRequest request)
      → ITransactions を Context.ServiceProvider から取り
      → RunAsync(Context, tx => DrawAsync(tx, request)) を返す
  protected abstract Task<DrawResponse> DrawAsync(TxContext tx, DrawRequest request)
```

*   `ITransactions` は `Context.ServiceProvider` から取る。コンストラクターで受け取らないので、アプリのコンストラクターは自由に書ける
*   `GetAuthContext()` など `Context` の拡張は今のまま使う
*   `ITransactions.RunAsync` は public のまま残す。生成したコードはアプリのアセンブリに入るため
*   `ThrowIfUnfilled` は残す。`AddCommonResponse` の登録忘れと `TCommon` の食い違いは、生成では防げない
*   基底クラスに RPC の実装を置いても、`RpcSettings` は属性を読める(`FilterTests` の `SkipAuth_OnInterfaceMethod_WorksWhenImplementedInBaseClass` で確かめ済み)
*   **迂回の防止**: `ServiceBase<T>` を直接継承すれば `RunAsync` を迂回できる。直接継承を止めるテストの例を利用ガイドに書き、アプリのテストで止める。手書きが要るサービスは、テストの除外一覧に書く。基盤の `DeviceService` は手書きのまま
*   **メソッド単位のフィルター**: アプリが `DrawAsync(TxContext, ...)` に付けたフィルター属性は、MagicOnion に読まれないおそれがある(`ServiceContext.AttributeLookup` は RPC のメソッドの情報。未確認)。A2 で確かめて、効かなければ Non-Goals に書く

#### クライアントの生成物

`[GenerateVoltechApi(typeof(IGachaService))] public sealed partial class GachaApi { }` から(Unity は C# 9 なので、本体なしのクラスは書けない)、次を生成する。

```text
sealed partial class GachaApi : ApiServiceBase<IGachaService>
  public GachaApi(ApiCore core)                      // ApiClientFactory で作ったクライアントを使う
  public static GachaApi CreateForTest(ApiCore core, IGachaService client) // テスト用の偽物を渡す
  public UniTask<ApiResult<DrawResponse>> DrawAsync(
      DrawRequest request, IEnumerable<ErrorCode>? handledCodes = null, bool notifyActivity = true)
```

*   `[SkipAuth]`・`[NoSideEffects]` は生成時に読み、`RpcSettings` の値を生成コードに埋め込む。`RpcSettings.CreateAll` のリフレクションと、`docs/index.md` の未確認の項目(属性を IL2CPP で読めるか)は無くなる
*   引数が複数の RPC は、今と同じくタプルにまとめて `InvokeAsync` に渡す
*   `[GenerateVoltechApi]` は、MagicOnion の `[MagicOnionClientGeneration]` の代わりにならない。通信するクライアントは今までどおり MagicOnion のジェネレーターが作るので、アプリは `IGachaService` と `IDeviceService` をどちらも `[MagicOnionClientGeneration]` に書く。書き忘れはコンパイルでは分からず、ラッパーを作るときの `MagicOnionClient.Create<T>` で失敗するはず(未確認。A1 で確かめる)
*   生成コードはアプリのアセンブリに入るので、`ApiServiceBase` の protected メンバーと `RpcSettings` を公開にする。そのため手書きのラッパーも書けるが、対応しない。属性との一致もフィルターを通ることも保証しないので、利用ガイドでは生成だけを案内する
*   `CreateForTest` で渡すクライアントはフィルターを通らない。偽のクライアントはヘッダーを送らないので問題にならない。本物のクライアントを渡すと、認証が要る RPC は `UNAUTHENTICATED` になり、`[SkipAuth]` の RPC は `x-app-*` が黙って欠ける。誤って渡さないよう、公開のコンストラクターにせず、テスト用と分かる名前にする

### 2. クライアントのヘッダー付与(`IClientFilter`)

*   **クライアントを作る部品**: 新しい公開の型 `ApiClientFactory` が、`CallInvoker`・シリアライザー・`IMagicOnionClientFactoryProvider`(アプリごとの生成物)・`ApiCoreOptions` を受け取り、基盤のフィルターを付けて `MagicOnionClient.Create<T>` を呼ぶ。`ApiCore` より先に作るので、`IDeviceService` のクライアントも同じ部品で作れる。`ApiCoreBuilder` にも渡す。生成したラッパーの公開のコンストラクターと `DeviceTokenProvider` は必ずこの部品を通るので、フィルターの渡し忘れが無くなる。Monitor(設計書 4)はトークンの発行も見られる
*   **送る順番**: `ApiCore` が今と同じ順で動く。トークンを取る → 締め切りを計算する → 入れ物(下の `ApiCallContext`)に値を入れる → `call(...)`(`InvokeAsync` に渡す、RPC を呼ぶラムダ)で送る
*   **呼び出しごとの入れ物**: `ApiCallContext`(内部の型)が `Token`・`RequestId`・`GameDate` を持つ。アプリのバージョンは固定なので、フィルターが `ApiCoreOptions` から読む。`ApiCore` が `call(...)` の直前に `AsyncLocal` に入れ、`finally` で外す
*   **読む順番**: `ApiHeaderFilter` は `SendAsync` の入り口で入れ物を読む。フィルターは何も待たないので、読むのも付けるのも `call(...)` の中で終わる。`AsyncLocal` の値が null(`ApiCore` が値を入れていない)なら例外にする(黙ってヘッダーなしで送らない)
*   **付けるヘッダー**: `authorization: Bearer <token>`(トークンがあるときだけ)、`x-request-id`(あるときだけ)、`x-app-platform`・`x-app-version`(`ApiCoreOptions.AppVersion` が null でないときだけ)、`x-game-date`(持っているときだけ)。名前・値・条件は今の `ApiCore.CreateHeaders` と同じ
*   **トークン**: 取得・`Invalidate`・再認証は `ApiCore` のまま。`CorruptedAccountDataException` などはフィルターを通らないので、`ACCOUNT_*` の扱いは変わらない
*   **`WithOptions` に残るもの**: 締め切りとセッションのキャンセルだけ
*   **`IDeviceService`**: `DeviceTokenProvider` も `ApiClientFactory` で作ったクライアントを呼ぶ。値がすべて null の `ApiCallContext` を入れて呼ぶので、付くのは `x-app-platform`・`x-app-version` だけで、今と同じ
*   **未確認**: フィルターが `call(...)` の中で同期的に始まることを確かめたのは、.NET の動的クライアントだけ。ソースジェネレーターのクライアント(Sandbox の `SmokeClient`)で A1 として確かめる
*   **テストの分け方**: 偽の `CallInvoker` と本物の `MagicOnionClient` で `ApiHeaderFilter` を通すテストを足し、ヘッダー・再認証・再送で同じ `x-request-id` を使うことをこの経路で確かめる。偽のクライアントを使う今の `ApiCoreTests` はフィルターを通らないので、ヘッダーの確認はこちらへ移す

### 3. 共通レスポンス項目の反映

*   `IPlayerDiffApplier<TCommon>`・`IBadgeApplier<TCommon>`・`INotificationApplier<TCommon>` を消し、`ICommonResponseApplier<TCommon>`(`UniTask ApplyAsync(TCommon common)`)を 1 つ置く
*   `ApiCoreBuilder.WithCommonResponseApplier<TCommon>(applier)` で渡す。省略できる
*   `Build` での `TCommon` の食い違いの検査は消す。反映処理は 1 つしか渡せないので、反映処理どうしの食い違いは起きない。反映処理とレスポンスの `TCommon` の食い違いは、今と同じく検出せず、反映されない。1 つのアプリの `TCommon` は 1 つなので、これを受け入れる
*   `CommonApplier` は消す。`WithCommonResponseApplier` の中のクロージャーが、レスポンスから `Common` を取り出して渡す
*   反映の位置と例外の扱いは今と同じ: レスポンスヘッダーを反映した後、順番待ちの中で呼ぶ。古いセッションでは呼ばない。例外は `CLIENT_ERROR` にして共通エラーハンドラーに渡し、送り直さない
*   ログの文言から「どの反映処理か」は無くなる。スタックトレースで分かる
*   player_diff → バッジ → 通知の順は、仕様の「項目ごとの決めごと」(通信の取り決めではなく、アプリが守る決めごと)に移す

### 4. 保存済みのレスポンス

コードは変えない。読めないときは `Deserialize` の例外が `RpcLogFilter` で `INTERNAL` になる。仕様に次を書く: 再送は失敗する。処理はコミット済みなので再実行しない。Retry では直らず、タイトルに戻るまで同じ失敗が続く。タイトルに戻れば、サインインで取り直せる。その応答の通知は失われる。型のキーの意味を変えたデプロイの前後では起こりうる。値がずれて読めた場合は検出しない。

### 5. AOT(IL2CPP)で動く前提

*   `TCommon` は `class` に限る(参照型なのでコードを共有できる)
*   `response is IApiResponse<TCommon>` は、インターフェースの型の判定で、実行時のコード生成を使わない
*   フォーマッターは、具体的なレスポンスの型ごとに MessagePack のジェネレーターが作る。`IApiResponse<>` そのもののフォーマッターは要らないはず(推測。下で確かめる)
*   クライアントの実行時のリフレクションは、ラッパーの生成で無くなる
*   `AsyncLocal` は `ApiHeaderFilter` が同期的に読むだけなので、待機をまたいだ流れには頼らない

`TCommon` の判定、フォーマッター、`AsyncLocal` の読み取りの 3 つを、Windows の IL2CPP のプレイヤーで確かめる。iOS の AOT は Mac で確かめる(RFC の未確認の項目)。どちらも Verification Plan に書く。

### 変えないもの

ヘッダーの名前と値、ErrorInfo、予約エラー、冪等性の意味、`Common` が null なら変化なしという意味、再送で同じ `Common` を返すこと、`ICommonResponseCollector<TCommon>`・`AddCommonResponse`・`TxContext.GetCollector`。

## Alternatives Considered

### 共通レスポンス項目の詰め方

#### Alternative A: サービスフィルターで `Common` を詰める

*   **Pros**: MagicOnion の流儀に合う。`RunAsync` を通らない RPC でも詰められる
*   **Cons**: フィルターは本体の外で動くので、保存とコミットの後になる
*   **Reason for Rejection**: 保存するレスポンスに `Common` が入らず、再送で同じ `Common` を返せない。冪等性の取り決めが崩れる

### `Common` が黙って空になる問題への対処(Q1)

#### Alternative B: フィルターで実行時に検査する

*   **Pros**: 部品は小さい。手書きのハンドラーのまま使える。`ServiceBase<T>` を直接継承したサービスも検出できる。`ServiceContext.GetRawResponse()` で読める
*   **Cons**: 動かしていない RPC では気づかない。検出はコミットの後になる
*   **Reason for Rejection**: オーナーは、失敗を見つけるより、書けない形にすることを選んだ

#### Alternative C: 全 RPC で `RunAsync` を実行時に必須にする

*   **Pros**: トランザクションの外の書き込みも見つかる
*   **Cons**: DB を使わない RPC を外す属性が要る。公開 API が増える
*   **Reason for Rejection**: 生成で全 RPC が `RunAsync` を通るので、目的が重なる

#### Alternative D: Roslyn アナライザーで検出する

*   **Pros**: 実行しなくても見つかる。実行時のコストが無い
*   **Cons**: `RunAsync` の外で詰め替える形は、`RunAsync` を呼んでいるので見逃す。別メソッドに分けた呼び出しも追えない
*   **Reason for Rejection**: 主な失敗の形を検出できない

#### Alternative E: 検査しない

*   **Pros**: 部品が増えない
*   **Cons**: 破ると、エラーなしにクライアントの表示がずれる
*   **Reason for Rejection**: 通知は取り直せず、原因にも気づきにくい

### 生成の範囲

#### Alternative F: サーバーだけ生成する

*   **Pros**: Unity にジェネレーターの DLL を配らなくてよい。`ApiCore` とラッパーの変更が減る
*   **Cons**: サーバーだけ生成になり、作り方がそろわない。属性のリフレクションの未確認の項目が残る
*   **Reason for Rejection**: オーナーは両側の作り方をそろえることを選んだ。共有インターフェースを読む部分はサーバーの生成で作るので共有でき、属性のリフレクションの未確認の項目も無くなる

#### Alternative G: 参照先まで走査して、全部を自動で生成する

*   **Pros**: 属性を書かなくてよい
*   **Cons**: `IDeviceService` を除外する仕組みが要る。走査は重い。Unity とサーバーで見えるアセンブリが違う
*   **Reason for Rejection**: MagicOnion と同じ、属性で明示する形を選んだ

#### Alternative H: トランザクションを開かない RPC を属性で外す

*   **Pros**: DB を使わない RPC も生成の仕組みのまま書ける
*   **Cons**: 属性・`RpcSettings`・ジェネレーターの分岐が増える。Uta の 7 RPC に使うものが無い
*   **Reason for Rejection**: 使う RPC が無い。必要になればサービスを手書きにできる

### 保存済みのレスポンス(Q2)

#### Alternative I: 保存の行に版を持たせる

*   **Pros**: 値がずれて読める場合も防げる
*   **Cons**: 列・マイグレーション・設定が増える。上げ忘れると意味が無い。合わないときの結果は同じ失敗
*   **Reason for Rejection**: 1 プレイヤー最新の 1 件だけで、起きるのはデプロイをまたいだ再送だけ。失敗の結果も変わらない

### クライアントの反映処理(Q3)

#### Alternative J: VolTech-gRPC 版と同じ 3 つのまま残す

*   **Pros**: 順序を基盤が保証する。名前がそろう。実装済みのコードとテストをそのまま使える
*   **Cons**: 基盤が `TCommon` の中身の区別を持つ。`TCommon` の食い違いの検査が残る
*   **Reason for Rejection**: 基盤は中身を知らない前提なので、順序はアプリの取り決めにする。RFC の規則でも、gRPC 版にそろえるのは最上位ではない

### ヘッダーの付け方(Q4)

#### Alternative K: `WithOptions` のまま残す

*   **Pros**: 変更が無い。受け渡しの仕組みが要らない。Monitor はフィルターからヘッダーを読める
*   **Cons**: 公式の書き方から外れたまま
*   **Reason for Rejection**: RFC の判断の規則では、書き方で迷ったら MagicOnion の公式に沿う。呼び出しに共通するヘッダーをフィルターで付けるのが公式の書き方なので、オーナーはそれに合わせることを選んだ

#### Alternative L: 固定の値だけフィルターに移す

*   **Pros**: `AsyncLocal` が要らない。同期開始に依存しない
*   **Cons**: ヘッダーを付ける場所が 2 か所に分かれる
*   **Reason for Rejection**: ヘッダーを付ける場所を 1 か所にするため。分けると、送る内容を知るのに `ApiCore` とフィルターの両方を読むことになる。`AsyncLocal` と同期開始への依存は、この 1 か所のために受け入れる(オーナーが決定)

#### Alternative M: トークンの取得もフィルターに移す

*   **Pros**: 認証の処理がフィルターに 1 本化される
*   **Cons**: 締め切りはフィルターから変えられないので、トークン取得の時間が RPC の締め切りを消費する。使ったトークンを `ApiCore` へ返す経路が要る。`ACCOUNT_*` の例外の型がフィルターを通る
*   **Reason for Rejection**: 締め切りの動きが変わる。今の `ApiCore.SendOnceAsync` は、トークンを取った後に締め切りを計算している

### アプリのフィルター(Q5)

#### Alternative N: アプリがフィルターを基盤より内側に足せる

*   **Pros**: アプリ固有の処理を基盤を直さずに足せる
*   **Cons**: 公開 API が増える。アプリのフィルターが再試行や打ち切りをすると、順番待ち・再認証・冪等性の前提を崩せる
*   **Reason for Rejection**: 今の使い道が無い。必要になったら足す

--------------------------------------------------------------------------------

## Non-Functional Requirements

### Security & Privacy

*   **トークン**: アクセストークンは `call(...)` の間だけ `ApiCallContext` に入り、`AsyncLocal` の値はその後 null に戻す。プロセスの中のメモリーにあり、今の `Metadata` と扱いは変わらない。`IApiLogger` には出さない。Monitor(設計書 4)はフィルターから `authorization` を読めるので、表示で隠すことを設計書 4 で決める
*   **コミットする DLL**: コンパイルのときに動く DLL をバイナリのまま配るので、差分でレビューできない。作るのはこのリポジトリのソースからだけで、`CommittedDllTests` が生成結果を比べる。完全な一致は保証しない。基盤は個人開発で、DLL を更新する人とソースを直す人が同じなので、これを受け入れる

### Rollout & Rollback

*   クライアントとの取り決めも DB のスキーマも変えないので、サーバーとクライアントはどちらを先に更新しても通信できる
*   基盤の公開 API は壊れる。Uta は基盤の版を上げるときに一緒に移す。戻すときは基盤の版を戻す

### Risks

*   **IL2CPP の確認が最後になる**: オーナーの決定で、A6・A5 と A1 の IL2CPP の分を段階 8 に回した。同期開始が IL2CPP だけで違えば、クライアントの変更が終わった後に Q4 を決め直す。.NET と Editor の結果が同じなら、起きる見込みは低いと見て受け入れる
*   **本体は全部トランザクションの中で動く**: 本体は DB の接続を持ったまま動く。冪等性の対象の RPC なら、`last_responses` の行ロックも持つ。そのため本体で外部 I/O をしない(Cloud SQL の接続上限に関わる。RFC の Analysis 10)。トランザクションの外で処理が要る RPC が出たら、そのサービスを手書きにする。Uta の 7 RPC は今も全部を `RunAsync` の中で動かしている
*   **手書きのサービスでは問題 1 が残る**: 手書きにする単位はサービスなので、1 RPC のためにサービスの全 RPC が保護を失う。RPC だけを別の共有インターフェースへ移すと、通信上のサービス名が変わる。手書きのサービスでは、`IApiResponse<>` を返す RPC のレスポンスを `RunAsync` の本体から返す規則を守る(利用ガイドに書く)。外部の検証が要る RPC(課金のレシートの検証など)で実際に使うことになる
*   **直接継承を止めるテストはアプリに置く**: 基盤の完了の時点では効かない。Uta を移すときに足す
*   **生成したサービスの本体で `RunAsync` を呼ぶと止まる**: `RunAsync` は public のままなので呼べる。内側のトランザクションが別の接続で同じ `last_responses` の行を待ち、外側は内側を待つ。DB はデッドロックと判定できず、締め切りまで止まる。手書きから移すときに残りうる形なので、利用ガイドに書く
*   **同期開始への依存**: `ApiHeaderFilter` が `call(...)` の中で同期的に始まらないと、入れ物を読めない。読めなければ例外になるので、黙ってヘッダーなしで送ることはない。ソースジェネレーターのクライアントでの確認は、実装の最初に A1 として行う
*   **コミットした DLL の古さ**: ジェネレーターのソースを直して DLL の更新を忘れると、Unity だけ古い生成物で動く。`CommittedDllTests` で、手元でも CI でも検出する。テストの入力に無い形の違いは検出できない
*   **属性の読み方が 2 つになる**: サーバーは実行時のリフレクション、クライアントはジェネレーターで `[SkipAuth]`・`[NoSideEffects]` を読む。食い違わないことをテストで確かめる

--------------------------------------------------------------------------------

## Detailed Implementation Plan

### 決めたこと

レビューで Detailed Implementation に回した項目を、ここで決める。

*   **受け継いだメソッド**: 共有インターフェースに直接書いたメソッドだけを対象にする。`IService<T>` と `IServiceMarker` 以外の基底インターフェースにメソッドがあれば、コンパイルエラーにする。今の仕様でも対応していない形。生成に変えると、クライアントのラッパーにそのメソッドが出ないだけになり気づけないので、コンパイルで止める
*   **引数の渡し方**: 下の表のとおり。クライアントの公開メソッドは、RPC の引数をそのまま並べ、最後に `IEnumerable<ErrorCode>? handledCodes = null, bool notifyActivity = true` を足す

| RPC の引数 | サーバーの `abstract` メソッド | クライアントの `InvokeAsync` |
| --- | --- | --- |
| 0 個 | `DrawAsync(TxContext tx)` | `request` は `object` の `null`。`call` は `static (c, _) => c.DrawAsync()` |
| 1 個 | `DrawAsync(TxContext tx, int count)` | 引数をそのまま渡す。`call` は `static (c, r) => c.DrawAsync(r)` |
| 2 個以上 | `DrawAsync(TxContext tx, int count, string name)` | タプルにまとめる。`call` は `static (c, r) => c.DrawAsync(r.Item1, r.Item2)` |

*   **属性を置くアセンブリ**: `[GenerateVoltechService]` は `VolTech.Server`、`[GenerateVoltechApi]` は `VolTech.MagicOnion.Client` に置く。使うのはそれぞれの側だけなので、`Abstractions` には置かない。ジェネレーターは属性を型の完全な名前(metadata name)で探し、`VolTech.Server` も Unity のコードも参照しない
*   **コミットした DLL の古さの検査**: コミットした DLL と今のソースのジェネレーターを同じ入力で動かし、生成結果とエラーが同じかをテストで比べる(`CommittedDllTests`)。バイト列の比較は、SDK の版が違うと内容が同じでも一致しないので使わない。DLL の更新は、`-p:UpdateUnityAnalyzer=true` を付けて Release でビルドしたときだけ行う。普通のビルドでは写さないので、アプリがジェネレーターを参照してビルドしても、サブモジュールの DLL は書き換わらない。CI も普通のビルドなので、コミットした DLL を検査できる。`.github/workflows/ci.yml` は変えない
*   **ログの文言**: 「どの反映処理か」が消えることは Q3 の結果として受け入れる。`IApiLogger` の仕組みは変えない

### 実装の順番

1.  **設計書を承認する**: オーナーが承認してから実装に入る。Status を Approved にし、一覧に載せる(ファイル 1・2)
2.  **IL2CPP を使わない項目を確かめる**(下の表)。A2 → A3 → A1 の Editor の分 → A4 の順に行う。本体のコードを直す前に行い、結果を設計書に戻す。確認に使うコードは手元に置き、該当する段階でコミットする。A6・A5 と A1 の IL2CPP の分は段階 8 で行う(2026-11-07 にオーナーが決定)
3.  **ドキュメント**: `docs:` のコミットを先に置く(ファイル 1 の A1〜A6 の結果、ファイル 3〜13)
4.  **反映処理**: 反映処理を 1 つにまとめる(ファイル 36・37 の「段階 4」、38、39、42 の `CommonResponseTests`)。`CommonApi` はこの段階では手書きのまま
5.  **ジェネレーター**: プロジェクト、属性、テスト、DLL の配布(ファイル 14〜26)。`GeneratorTests` が `[GenerateVoltechService]` を、`RpcSettingsConsistencyTests` が `InternalsVisibleTo` を使うので、ファイル 25・26 はここに入れる
6.  **サーバー**: 生成の利用(ファイル 27〜30)
7.  **クライアントのラッパーとヘッダー**: `ApiClientFactory`・`ApiHeaderFilter`・`RpcSettings`・ラッパーの生成(ファイル 31〜35、36・37 の「段階 7」、40〜44)
8.  **Sandbox**: A6 → A1 の IL2CPP の分・A5 の順に行い、AOT の確認(ファイル 45〜47)をする。Verification Plan を実行し、結果と実装で分かった API 名の違いを設計書と `docs/index.md` に戻す(ファイル 1・12)

実装は 1 本のブランチで行い、段階ごとにテストが通る形でコミットする。Uta の変更はこの設計書に含まない。基盤の版を上げるときに、Uta のリポジトリで 7 RPC のサービスとラッパー、組み立ての処理を移す。

#### 最初に確かめる項目

| # | 確かめること | 方法 | 結果ごとの対応 |
| --- | --- | --- | --- |
| A6 | Unity Test Framework のテストを、Windows の IL2CPP のプレイヤーで動かせるか(7000.0.0a12、`-testPlatform StandaloneWindows64`) | 1 件だけのテストで試す | 動かなければ、ファイル 46・47 のとおり作る。動けば、A1・A5 はそのテストと同じ仕組みで確かめ、`AotCheck` の代わりに標準のプレイヤーテストを使う案(asmdef の分け方と Verification Plan の書き直し)をオーナーに出す。決まるまでファイル 46・47 は作らない |
| A1 | ソースジェネレーターのクライアントで、先頭のフィルターが RPC のメソッドの呼び出しの中で同期的に始まるか。`AsyncLocal` を読めるか。ヘッダーを渡さず締め切りだけの `CallOptions` でも、`context.CallOptions.Headers` が null でないか。`[MagicOnionClientGeneration]` に書いていないインターフェースで `MagicOnionClient.Create<T>` がどう失敗するか | `SmokeClient` と同じくソースジェネレーターで作ったクライアントを、偽の `CallInvoker` で呼ぶ。Editor の分は A3 の EditMode のテストで確かめる。IL2CPP の分は段階 8 で確かめる | 同期開始か `AsyncLocal` の読み取りが違えば: ヘッダーの移行を止め、Q4 をオーナーと決め直す(Alternative L か K)。ラッパーの生成と `ApiClientFactory` の形も、その結果で決め直す。反映処理とサーバーの生成は Q4 に左右されないので進める。`Headers` が null なら: 移行は止めず、`ApiCore` と `DeviceTokenProvider` が空の `Metadata` を渡す。`Create<T>` の失敗の仕方は、結果を利用ガイドに書く |
| A5 | Windows の IL2CPP のプレイヤーで、`IApiResponse<TCommon>` を実装したレスポンスの MessagePack の往復と、`response is IApiResponse<TCommon>` が動くか | A1 と同じ `AotCheck` に入れる(ファイル 45〜47) | フォーマッターが動かなければ MessagePack の設定を直す。型の判定が動かなければ、反映処理の渡し方をオーナーと決め直す。影響はクライアントの反映処理に収まる |
| A2 | アプリの `protected override` のメソッドに付けたフィルター属性が効くか。RPC の `public` メソッドと同じ名前の `protected abstract` メソッドがあっても、MagicOnion が RPC を正しく登録するか | 生成物と同じ形のサービスを手で書き、`FilterTests` で動かす(ファイル 29)。共有インターフェースに `[SkipAuth]` を付け(付けないと認証のフィルターで先に `UNAUTHENTICATED` になる)、DB を使わない。業務エラーを投げるフィルターを `protected override` に付け、そのエラーが返れば、属性が効いていて RPC も登録されている。`Unimplemented` なら登録されていない。`RunAsync` の接続の失敗(`INTERNAL`)なら属性が効いていない | 属性が効かなければ Non-Goals に書き、利用ガイドにも書く。名前が使えなければ、メソッドの名前を変える案をオーナーに出す(CUJ 1 が変わる) |
| A3 | 本物の `MagicOnionClient` と偽の `CallInvoker` を EditMode で動かせるか | `FakeCallInvoker` を作り、`MagicOnionClient.Create<T>` にテスト用のフィルターを渡す最小のテストを書く(ファイル 43 の `FakeCallInvoker`、44)。`ApiClientFactory` は段階 7 で作るので、ここでは使わない | 動かなければ、テストの分け方をオーナーと決め直す |
| A4 | パッケージの `Analyzers/` に置いた `RoslynAnalyzer` ラベルの DLL が、Sandbox の `Assets/` のコードとテストの asmdef に効くか | 目印の型(`VolTech.Generated.AnalyzerMarker`)を 1 つ生成するだけの DLL をリポジトリの外で作って置き、Sandbox の `Assets/` のコードとテストの asmdef から、その型を参照してコンパイルできるかを見る。確かめたら DLL は消す | 効かなければ置き場所を変える案を出す。Uta の DLL は `Assets/` の下にあり、パッケージの中に置く形は確かめていない |

結果は、この設計書の該当する節に戻し、`docs/index.md` の「確かめること」に日付と方法を添えて書く。

### Target Files to Create/Modify

`Unity/` の下に足すファイルの `.meta` は Unity が作るので、ファイルと一緒にコミットする。消すファイルの `.meta` も一緒に消す。下の一覧には個別に挙げない。例外はファイル 23 で、`.meta` を手で書く。

#### ドキュメント

#### 1. `docs/designs/common-response-and-client-filter.md` (Modify)

*   **Type / Scope**: 設計書
*   **Proposed Changes**: 段階 1 で Status を Approved にする。段階 2 の結果(A1〜A6)を段階 3 で、実装で分かった API 名の違いを段階 8 で戻す

#### 2. `docs/designs/index.md` (Modify)

*   **Type / Scope**: 設計書の一覧
*   **Proposed Changes**: 段階 1 で、この設計書を「検討中」から「承認済み」に移す。CLAUDE.md の手順 4

#### 3. `docs/spec/common-response.md` (Modify)

*   **Type / Scope**: 仕様書
*   **Proposed Changes**: 反映処理が 1 つになるので、「クライアント」の節を直す。基盤が順序を保証する記述をやめ、「反映処理へ `Common` を渡す。省略できる」にする。「残りは呼ばない」を消す。`TCommon` の食い違いの項目を消す。順序はアプリの決めごととして、「項目ごとの決めごと」に足す(player_diff → バッジ → 通知)。「サーバー」の節に、生成した基底クラスが `RunAsync` を必ず通すことを足す

#### 4. `docs/spec/headers.md` (Modify)

*   **Type / Scope**: 仕様書
*   **Proposed Changes**: ヘッダーの表は変えない。ラッパーの説明を生成に直す。受け継いだメソッドの `ArgumentException` をコンパイルエラーに直す。`RpcSettings` の説明を、クライアントはジェネレーターが生成時に読む形に直す

#### 5. `docs/spec/transaction.md` (Modify)

*   **Type / Scope**: 仕様書
*   **Proposed Changes**: 「トランザクション」の節を、生成した基底クラスが `RunAsync` で包む形に直す。「冪等性」の節に Q2 の決定を書く: 保存済みのレスポンスを読めなければ `INTERNAL`。処理はコミット済みなので再実行しない。Retry では直らず、タイトルに戻るまで同じ失敗が続く。タイトルに戻ればサインインで取り直せる。その応答の通知は失われる。値がずれて読めた場合は検出しない

#### 6. `docs/designs/implementation/client.md` (Modify)

*   **Type / Scope**: 実装設計書
*   **Proposed Changes**: 冒頭の表の `WithOptions` でヘッダーを渡す記述、反映処理 3 つの行、`RpcSettings` の行を直す。「移したもの」の表の `ApiCore`・`ApiServiceBase`・`RpcSettings`・`DeviceTokenProvider` の行を、`ApiClientFactory` と生成の形に直す。「ラッパーの書き方」と「共通レスポンス項目の反映」を、この設計書へのリンクに置き換える。「今の規模では手間に見合わない」の判断も一緒に消える。「移していないもの」の `FakeCallInvoker` の説明を、ファイル 43 の方式に合わせて直す。「テスト」の節を、ファイル 41〜43 の内容に合わせる

#### 7. `docs/designs/implementation/server.md` (Modify)

*   **Type / Scope**: 実装設計書
*   **Proposed Changes**: フィルターの表の「ハンドラー」の行に、生成した基底クラスが `RunAsync` で包むことを書く。「共通レスポンス項目」の節に、この設計書へのリンクを足す。コレクターの表と説明は、この設計書が変えないので残す

#### 8. `docs/designs/implementation/project-structure.md` (Modify)

*   **Type / Scope**: 実装設計書
*   **Proposed Changes**: フォルダーの図と表に `src/VolTech.Generators`・`tests/VolTech.Generators.Tests`・パッケージの `Analyzers/` を足す。「2 つに分ける」の例外として、ジェネレーターは Roslyn の都合(netstandard2.0)で別のプロジェクトにすることを書く。DLL の更新の手順を書く

#### 9. `docs/guide/client.md` (Modify)

*   **Type / Scope**: 利用ガイド
*   **Proposed Changes**: 「組み立て」を `ApiClientFactory` を作る形に直す。「共通レスポンス項目の反映」を `WithCommonResponseApplier` に直し、順序はアプリが守ると書く。「ラッパー」を `[GenerateVoltechApi]` に直し、`[MagicOnionClientGeneration]` にも同じ共有インターフェースを書くことと、忘れたときの失敗の仕方を書く。`new GachaApi(core)` で作り、`CreateForTest` に本物のクライアントを渡さないことを書く。「VolTech-gRPC 版から移すとき」の、手でラッパーを書く記述、`DeviceTokenProvider` の引数の記述、3 つの反映処理の記述を直す

#### 10. `docs/guide/server.md` (Modify)

*   **Type / Scope**: 利用ガイド
*   **Proposed Changes**: 「ハンドラー」を `[GenerateVoltechService]` と `DrawAsync(TxContext tx, ...)` の形に直す。本体で外部 I/O をしないこと、本体で `RunAsync` を呼ばないこと、手書きのサービスで守る規則を書く。`ServiceBase<T>` の直接継承を止めるアプリのテストの例を書く。判定: `ServiceBase<>` を継承した具象クラスごとに基底をたどり、`ServiceBase<>` のすぐ下のクラスに `[GenerateVoltechService]` が付いていなければ違反。除外一覧にあれば通す。手書きが要るサービスは除外一覧に書くと説明する。受け継いだメソッドの記述を、生成を使うとコンパイルエラー(`VTG004`)になる形に直す

#### 11. `docs/guide/getting-started.md` (Modify)

*   **Type / Scope**: 利用ガイド
*   **Proposed Changes**: アプリのサーバーの csproj に、`src/VolTech.Generators` の `ProjectReference` を `OutputItemType="Analyzer"`・`ReferenceOutputAssembly="false"` で足す手順を書く。Unity は DLL がパッケージに入っているので、ジェネレーターを入れる作業は要らないと書く(`[MagicOnionClientGeneration]` は今までどおり要る)

#### 12. `docs/index.md` (Modify)

*   **Type / Scope**: 計画とチェックリスト
*   **Proposed Changes**: 表に `VolTech.Generators` を足し、Unity クライアントの行の `WithPlayerDiffApplier` などを直す。「確かめること」に A1〜A6 を足す。`ApiServiceBase` が属性をリフレクションで読めるかの項目は、リフレクションが無くなったので「不要になった」と書いて閉じる。「決めること」の反映処理の記述(「3 つ」)を直す。「進め方」は経過の記録なので直さない

#### 13. `README.md`・`CHANGELOG.md` (Modify)

*   **Type / Scope**: 入口の文書
*   **Proposed Changes**: `README.md` の場所の表に `src/VolTech.Generators/` を足す。「開発」に、ジェネレーターを直したら `dotnet build src/VolTech.Generators --configuration Release -p:UpdateUnityAnalyzer=true` で DLL を更新してコミットすると書く。`CHANGELOG.md` の「互換性が壊れる変更」に、`ApiCore.CreateBuilder` の引数、`ApiServiceBase`、`DeviceTokenProvider`、反映処理を書く。「Added」にジェネレーターと `ApiClientFactory` を書く

#### ジェネレーター

#### 14. `src/VolTech.Generators/VolTech.Generators.csproj` (Create)

*   **Type / Scope**: ビルドの設定
*   **Proposed Changes**: netstandard2.0、`LangVersion` は latest、`EnforceExtendedAnalyzerRules` と `IsRoslynComponent` を有効にする。`Microsoft.CodeAnalysis.CSharp` を `PrivateAssets="all"` で参照する。`InternalsVisibleTo` に `VolTech.Generators.Tests` を足す。ビルド後の処理で、`$(Configuration)` が Release かつ `$(UpdateUnityAnalyzer)` が `true` のときだけ、DLL を `Unity/com.chinpangx.voltech.magiconion/Analyzers/` に写す

#### 15. `src/VolTech.Generators/ServiceModelReader.cs` (Create)

*   **Type / Scope**: ジェネレーターの共通部分
*   **Proposed Changes**: 属性の `typeof` から共有インターフェースのシンボルを取り、比較できる値(インクリメンタル生成のキャッシュに使う)に変える。名前空間、クラス名、サービス名、メソッドごとの名前・レスポンスの型・引数・`skipAuth`・`noSideEffects` を持つ。`[SkipAuth]` はインターフェースかメソッド、`[NoSideEffects]` はメソッドの属性を読む。基底インターフェースの属性は読まない(サーバーの `RpcSettings` と同じ)。下のエラーをここで判定する

| ID | 条件 | 対象 |
| --- | --- | --- |
| `VTG001` | ネストしたクラス、またはジェネリックのクラス | 両方 |
| `VTG002` | `typeof` の型が `IService<自分>` を継承したインターフェースでない、または `IStreamingHubMarker` を継承している(StreamingHub) | 両方 |
| `VTG003` | `UnaryResult<T>` 以外を返すメソッド(戻り値なしの `UnaryResult` を含む)。`ref`・`out`・`in` の引数か、型引数を持つメソッド | 両方 |
| `VTG004` | `IService<T>`・`IServiceMarker` 以外の基底インターフェースにメソッドがある | 両方 |
| `VTG005` | 同じ名前のメソッドが複数ある(MagicOnion はオーバーロードに対応しない) | 両方 |

`partial` が無いクラスと、生成コードの引数と重なる名前の引数(サーバーは `tx`、クライアントは `handledCodes`・`notifyActivity`)は、コンパイラーのエラー(CS0260・CS0100)で止まるので、ジェネレーターでは判定しない。引数の既定値は生成コードに写さない。

#### 16. `src/VolTech.Generators/VoltechServiceGenerator.cs` (Create)

*   **Type / Scope**: サーバー用のジェネレーター
*   **Proposed Changes**: `[GenerateVoltechService]` の付いたクラスの `partial` を生成する。形は Technical Plan の「サーバーの生成物」。RPC の `public` メソッドは `async UnaryResult<T>` で、`Context.ServiceProvider` から `ITransactions` を取り、`RunAsync(Context, tx => DrawAsync(tx, ...))` を `await` した値を返す。`protected abstract Task<T>` を一緒に生成する。型はすべて `global::` 付きで書く。生成コードのローカル変数とラムダの引数は `__` で始め、RPC の引数と重ならないようにする。ファイルの先頭に `// <auto-generated/>` と `#nullable enable` を書く。ヒント名は `<名前空間>.<クラス名>.g.cs`

#### 17. `src/VolTech.Generators/VoltechApiGenerator.cs` (Create)

*   **Type / Scope**: クライアント用のジェネレーター
*   **Proposed Changes**: `[GenerateVoltechApi]` の付いたクラスの `partial` を生成する。形は Technical Plan の「クライアントの生成物」。`private GachaApi(ApiCore core, IGachaService client)` を生成し、`CreateForTest` から呼ぶ。C# 9 で書く。メソッドごとに `RpcSettings` の `readonly` のインスタンスフィールドを持つ(Unity 7 のアナライザー UAL0010 に当たらないよう、static にしない)。`IsIdempotencyTarget` は `!noSideEffects && !skipAuth`、`RequiresAuth` は `!skipAuth`。`RpcSettings.MethodName` の値は `IGachaService/DrawAsync` の形。ファイルの先頭とヒント名はファイル 16 と同じ

#### 18. `tests/VolTech.Generators.Tests/VolTech.Generators.Tests.csproj` (Create)

*   **Type / Scope**: テストの設定
*   **Proposed Changes**: xunit のプロジェクト。xunit の参照と `<Using Include="Xunit" />` は `VolTech.Server.Tests.csproj` と同じにする。`Microsoft.CodeAnalysis.CSharp` を版なしの `PackageReference` で足す(ファイル 14 の参照は `PrivateAssets="all"` なので伝わらない)。`VolTech.Generators` を普通の参照、`VolTech.Server` をプロジェクト参照で持つ。Unity のクライアントはここでコンパイルできないので、`[GenerateVoltechApi]` と `ApiServiceBase<T>` などの最小の代役をテストの入力に入れる。実物とのコンパイルは EditMode(ファイル 41)で確かめる

#### 19. `tests/VolTech.Generators.Tests/GeneratorTests.cs` (Create)

*   **Type / Scope**: 単体テスト
*   **Proposed Changes**: `CSharpGeneratorDriver` で動かす。引数 0・1・2 個、`[SkipAuth]`(インターフェースとメソッド)、`[NoSideEffects]`、参照先のアセンブリの共有インターフェースで、生成結果が期待と一致することと、生成後のコンパイルにエラーが無いことを確かめる。`VTG001`〜`VTG005` がそれぞれ出ることと、`partial` が無いクラス(CS0260)と重なる引数名(CS0100)がコンパイルエラーになることを確かめる

#### 20. `tests/VolTech.Generators.Tests/RpcSettingsConsistencyTests.cs` (Create)

*   **Type / Scope**: 単体テスト
*   **Proposed Changes**: 属性の組み合わせを全部持つ共有インターフェースと、その実装クラスをテストのコードに置く。実行時は、実装のメソッドごとにサーバーの `RpcSettings.Create` を呼ぶ。生成時の値は、テストのアセンブリを参照する `CSharpCompilation` からインターフェースのシンボルを取り、`ServiceModelReader`(internal)で読む。`skipAuth`・`noSideEffects` が一致することを確かめる。Risks の「属性の読み方が 2 つになる」への対応

#### 21. `tests/VolTech.Generators.Tests/CommittedDllTests.cs` (Create)

*   **Type / Scope**: 単体テスト
*   **Proposed Changes**: `Unity/com.chinpangx.voltech.magiconion/Analyzers/VolTech.Generators.dll` を別の `AssemblyLoadContext` で読み、ファイル 19 と同じ入力で動かす。今のソースのジェネレーターと、生成結果・エラーが同じかを確かめる

#### 22. `VolTech-MagicOnion.slnx`・`Directory.Packages.props` (Modify)

*   **Type / Scope**: ビルドの設定
*   **Proposed Changes**: `.slnx` に `VolTech.Generators` と `VolTech.Generators.Tests` を足す。`Directory.Packages.props` に `Microsoft.CodeAnalysis.CSharp` 4.3.1 を足す。MagicOnion のジェネレーターと同じ Roslyn 4.3 系に合わせる

#### 23. `Unity/com.chinpangx.voltech.magiconion/Analyzers/VolTech.Generators.dll`、`Analyzers.meta`、`VolTech.Generators.dll.meta` (Create)

*   **Type / Scope**: Unity に配る DLL
*   **Proposed Changes**: ファイル 14 のビルドで作る。`.meta` は `labels` に `RoslynAnalyzer` を書き、`platformData` の `Any` と `Editor` を `enabled: 0` にする。形は Uta の `MagicOnion.Client.SourceGenerator.dll.meta` と同じ

#### 24. `.gitattributes` (Modify)

*   **Type / Scope**: Git の設定
*   **Proposed Changes**: `*.dll binary` を足す。今の `* text=auto eol=lf` では Git の判定に任せているので、コミットする DLL が変換されないことをはっきりさせる

#### サーバー

#### 25. `src/VolTech.Server/Hosting/GenerateVoltechServiceAttribute.cs` (Create)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `Type` を 1 つ受け取るクラス用の属性。名前空間は `VolTech.Server.Hosting`。`Inherited = false`、`AllowMultiple = false`

#### 26. `src/VolTech.Server/VolTech.Server.csproj` (Modify)

*   **Type / Scope**: ビルドの設定
*   **Proposed Changes**: `InternalsVisibleTo` に `VolTech.Generators.Tests` を足す。ファイル 20 が internal の `RpcSettings` を読むため

#### 27. `src/VolTech.Server/Transaction/ITransactions.cs` (Modify)

*   **Type / Scope**: XML コメント
*   **Proposed Changes**: アプリは生成した基底クラスを通し、直接呼ぶのは生成したコードと手書きのサービスだけ、と書く。メソッドは `public` のまま。動きは変えない

#### 28. `tests/VolTech.Server.Tests/VolTech.Server.Tests.csproj` (Modify)

*   **Type / Scope**: ビルドの設定
*   **Proposed Changes**: `VolTech.Generators` を `OutputItemType="Analyzer"`・`ReferenceOutputAssembly="false"` で参照する。アプリと同じ使い方で確かめるため

#### 29. `tests/VolTech.Server.Tests/FilterTests.cs`・`Support/TestServices.cs` (Modify)

*   **Type / Scope**: 結合テスト
*   **Proposed Changes**: A2 のテストを足す。生成物と同じ形のサービスを `[SkipAuth]` の共有インターフェースで手で書き、`protected override` に、既存の `ThrowBusinessFilter` と同じく業務エラーを投げるフィルター属性を付ける。`FilterTests` は DB を使わないので、本体に入る前にそのエラーが返ることを確かめる。ジェネレーターができたら、同じテストを生成したサービスに切り替える(同じく DB を使わない)。A2 で属性が効かなかったら、このテストは RPC の登録だけを確かめる形に直す

#### 30. `tests/VolTech.Server.Tests/GeneratedServiceTests.cs`・`Support/GeneratedTestServices.cs` (Create)

*   **Type / Scope**: 結合テスト
*   **Proposed Changes**: 生成した基底クラスのサービスで、`Common` が詰まること(CUJ 1)、同じ `x-request-id` の再送で本体を呼ばず同じ `Common` を返すこと、例外でロールバックすること、`[NoSideEffects]` が読み取り専用で動くこと、`[SkipAuth]` が冪等性の対象にならないことを確かめる。保存済みのレスポンスを読めないときに `INTERNAL` になり本体を呼ばないこと(CUJ 4)も確かめる。`GeneratedTestServices.cs` の名前空間は `VolTech.Server.Tests.Generated` にする。利用ガイドの「直接継承を止めるテスト」の検査も確かめる: この名前空間の型では違反が 0 件、`PublicTestService`(直接継承)を足すと 1 件、`DerivedPlacementService`(手書きの抽象クラスを挟む)を足しても 1 件、それらを除外一覧に入れると 0 件

#### クライアント

#### 31. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiClientFactory.cs` (Create)、`Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiChannel.cs` (Modify)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: コンストラクターは `CallInvoker`・`IMagicOnionSerializerProvider`・`IMagicOnionClientFactoryProvider`・`ApiCoreOptions` を受け取る。`AsyncLocal<ApiCallContext>` をインスタンスで 1 つ持ち(static にしない。外には見せない)、`ApiHeaderFilter` を作る。`internal T Send<T>(ApiCallContext context, Func<T> send)` は、`AsyncLocal` に値を入れて `send` を呼び、`finally` で null に戻す。`Create<T>()` は internal で、基盤のフィルターを付けて `MagicOnionClient.Create<T>(CallInvoker, IMagicOnionSerializerProvider, IClientFilter[], IMagicOnionClientFactoryProvider)` を呼ぶ(7.11.0 にあることをリフレクションで確かめた。2026-11-07)。コンストラクターの引数はどれも必須で、null なら `ArgumentNullException`。`Options` は internal のプロパティで `ApiCore`・`DeviceTokenProvider` に見せる。テスト用のコンストラクターは作らない。テストは `TestClients.CreateFactory`(ファイル 43)で同じコンストラクターを使う。`ApiChannel.cs` は、`CallInvoker` の渡し先の XML コメントを `MagicOnionClient.Create` から `ApiClientFactory` に直す

#### 32. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Internal/ApiHeaderFilter.cs`・`ApiCallContext.cs` (Create)

*   **Type / Scope**: 内部の型
*   **Proposed Changes**: `ApiCallContext` は `Token`・`RequestId`・`GameDate` を持つ変更できない型。`ApiHeaderFilter` は `IClientFilter` を実装し、`SendAsync` の入り口で `AsyncLocal` を読み、`context.CallOptions.Headers` に足してから次を呼ぶ。`AsyncLocal` が null なら `InvalidOperationException`。付けるヘッダーと条件は Technical Plan の 2 節のとおりで、`ApiHeaders` の定数と `AppVersion.AddHeadersTo` を使う

#### 33. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/GenerateVoltechApiAttribute.cs` (Create)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `Type` を 1 つ受け取るクラス用の属性。名前空間は `VolTech.Client`。`Inherited = false`、`AllowMultiple = false`

#### 34. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/RpcSettings.cs` (Create)、`Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Internal/RpcSettings.cs` (Delete)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: 生成コードはアプリのアセンブリに入るので、`public sealed class` にして `Internal/` から移す。コンストラクターと 3 つのプロパティ(`MethodName`・`IsIdempotencyTarget`・`RequiresAuth`)を `public` にする。リフレクションの `CreateAll` を消す

#### 35. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiServiceBase.cs` (Modify)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `ApiServiceBase(ApiCore core)` を足す。`ApiCore` の internal プロパティ `ClientFactory` でクライアントを作る。`ApiServiceBase(ApiCore core, TService client)` はテスト用に残す。`protected InvokeAsync` の第 1 引数を `string methodName` から `RpcSettings settings` に変え、メソッド名からの `Dictionary` と `ArgumentException` を消す

#### 36. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiCore.cs` (Modify)

*   **Type / Scope**: 共通処理
*   **Proposed Changes**: 段階 4: `commonAppliers` を `Func<object, UniTask>` 1 つ(`ApiCoreBuilder.ApplyCommonResponse`)に変え、`ApplyCommonFieldsAsync` のループと `applying` を消す。段階 7: `CreateBuilder` の第 1 引数を `ApiCoreOptions` から `ApiClientFactory` に変える(`ApiCoreOptions` は `ApiClientFactory.Options` から取る)。internal プロパティ `ClientFactory` を足す。`SendOnceAsync` の `CallOptions` にヘッダーを入れず、締め切りとキャンセルだけにする。`call(...)` は `ApiClientFactory.Send` の中で呼ぶ(`await` の前に `AsyncLocal` が null に戻る)。`CreateHeaders` を消す。処理の順番、再認証、順番待ち、`CLIENT_ERROR` の扱いは変えない

#### 37. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiCoreBuilder.cs` (Modify)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: 段階 4: `WithPlayerDiffApplier`・`WithBadgeApplier`・`WithNotificationApplier` と 3 つの internal プロパティを消し、`WithCommonResponseApplier<TCommon>(ICommonResponseApplier<TCommon> applier)` を足す。中のクロージャーが `response is IApiResponse<TCommon>` で判定し、`Common` が null でなければ `ApplyAsync` を呼ぶ。これを internal プロパティ `ApplyCommonResponse` に入れる。null を渡すと渡さなかったのと同じで、設定済みなら外れる。2 回呼ぶと後のほうで置き換わる。`Build` の検査と `CommonAppliers()` を消す。段階 7: コンストラクターの第 1 引数を `ApiClientFactory` に変え、`Options` プロパティを `ClientFactory` プロパティに変える

#### 38. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Hooks/ICommonResponseApplier.cs` (Create)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `UniTask ApplyAsync(TCommon common)` を持つ。`where TCommon : class`。XML コメントに、player_diff → バッジ → 通知の順で反映すること、データの更新だけをすることを書く

#### 39. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Hooks/IPlayerDiffApplier.cs`・`IBadgeApplier.cs`・`INotificationApplier.cs`、`Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Internal/CommonApplier.cs` (Delete)

*   **Type / Scope**: 公開 API と内部の型
*   **Proposed Changes**: Q3 の決定で不要になる。Uta はリリース前なので、残す期間は設けない

#### 40. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Auth/DeviceTokenProvider.cs` (Modify)

*   **Type / Scope**: 認証
*   **Proposed Changes**: 公開のコンストラクターを `(ApiClientFactory clientFactory, IAccountStore accountStore, Func<DateTime> utcNow = null)` にする。設定は `ApiClientFactory` から取り、`IDeviceService` のクライアントも作る。テスト用に、internal のコンストラクター `(IDeviceService client, ApiClientFactory clientFactory, IAccountStore accountStore, Func<DateTime> utcNow = null)` を残す。公開のコンストラクターはこれを呼ぶ。`RegisterAsync`・`IssueTokenAsync` は、値がすべて null の `ApiCallContext` を渡して `ApiClientFactory.Send` の中で呼ぶ。`CreateCallOptions` からヘッダーを消す

#### 41. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/ApiCoreTests.cs` (Modify)

*   **Type / Scope**: EditMode テスト
*   **Proposed Changes**: `TestApi` をトップレベルの `[GenerateVoltechApi(typeof(ITestService))] internal sealed partial class` に変える。生成コードが実物の基盤の型とコンパイルできることも、これで確かめる。`CreateAll` と未知のメソッドのテストを消す。設定はファイル 19・20 で確かめる。`CreateBuilder` には `TestClients.CreateFactory()` を渡し、`TestApi.CreateForTest` で偽のクライアントを渡す。偽のクライアントの `Options.Headers` は null になるので、ヘッダーの assert は次の 7 件から消し、ほかの確認は残す: `SendsAuthorizationAndRequestId`、`AppliesResponseHeaders`、`ResendsOnceAfterUnauthenticated`、`RetryAfterSecondUnauthenticatedUsesNewToken`、`SkipAuthRpcSendsNoTokenOrRequestId`、`ResendsOnceAgainAfterRetry`、`RetriesWithSameRequestIdWhenErrorHandlerSaysRetry`。消した assert は同じ内容でファイル 43 に足す。ヘッダーの assert だけを持つ `ResetSessionDropsTokenAndGameDate` は、ファイル 43 へ移す。締め切りとキャンセルの確認は、`WithOptions` に残るのでここに残す。入れ子の `FakeTokenProvider`・`RecordingErrorHandler` は、ファイル 43 の `Support/` のファイルへ移す

#### 42. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/CommonResponseTests.cs`・`DeviceTokenProviderTests.cs` (Modify)

*   **Type / Scope**: EditMode テスト
*   **Proposed Changes**: `CommonResponseTests` は反映処理を 1 つにする。`AppliesInOrder` は、反映処理 1 つが `Common` を受け取り、呼び出し側に返る前に反映が終わることの確認に書き直す。3 つのうち一部を渡すテスト(`OnlyGivenAppliersAreCalled`)と `TCommon` の食い違いのテストを消す。次を足す: 反映処理を渡さなければ反映しないこと、`WithCommonResponseApplier` に null を渡すと反映しないこと(設定済みなら外れる)、2 回呼ぶと後のものだけが動くこと、レスポンスが `IApiResponse<TCommon>` でない・違う `TCommon` のときは反映しないこと。入れ子の `CommonApi` は、段階 7 でトップレベルの `[GenerateVoltechApi(typeof(ICommonTestService))] internal sealed partial class` に変え、`CreateBuilder` に `TestClients.CreateFactory()` を渡し、`CommonApi.CreateForTest` で偽のクライアントを渡す。`Common` が null なら呼ばないこと、例外で `CLIENT_ERROR` になり送り直さないこと、古いセッションでは反映しないことは残す。`DeviceTokenProviderTests` は internal のコンストラクターに偽のクライアントと `TestClients.CreateFactory()` を渡し、`SendsAppVersion` のヘッダーの確認をファイル 43 へ移す。締め切りの確認はここに残し、テストの名前を `SendsDeadline` にする

#### 43. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/ApiHeaderFilterTests.cs`・`Support/FakeCallInvoker.cs`・`Support/TestClients.cs`・`Support/FakeTokenProvider.cs`・`Support/RecordingErrorHandler.cs` (Create)

*   **Type / Scope**: EditMode テスト
*   **Proposed Changes**: 本物の `MagicOnionClient` と偽の `CallInvoker` で、`ApiClientFactory` から作ったクライアントを通す。`FakeCallInvoker` はテストの中だけで使う(`Runtime/Client/Testing/` には置かない)。`AsyncUnaryCall<TRequest, TResponse>` だけを実装し、ほかは `NotSupportedException` にする。`method.FullName` と `options` を記録し、テストが積んだ結果を返す。成功の結果は、レスポンスを MessagePack にしたバイト列とレスポンスヘッダー。バイト列は `method.ResponseMarshaller` で読んで返す。失敗の結果は `RpcException`。バイト列で積むのは、MagicOnion が内部の型で通信するので、型付きの値を渡せないため。`TestClients` は `partial` のクラスで、`[MagicOnionClientGeneration(typeof(ITestService), typeof(ICommonTestService), typeof(IDeviceService))]` を付け、`CreateFactory(FakeCallInvoker callInvoker = null, ApiCoreOptions options = null)` を持つ。生成物と `MessagePackMagicOnionSerializerProvider` で `ApiClientFactory` を作る(`SmokeClient` と同じ形)。確かめること: `authorization`・`x-request-id`・`x-app-platform`・`x-app-version`・`x-game-date` の値と付く条件、`[SkipAuth]` の RPC に `authorization`・`x-request-id` が付かないこと、`x-game-date` を 2 回目以降も最初の値のまま送ること、`UNAUTHENTICATED` の再送で新しいトークンと同じ `x-request-id` を使うこと、`UNAUTHENTICATED` が 2 回続いた後のエラーハンドラーの Retry で新しいトークンを使うこと、Retry の後にまた `UNAUTHENTICATED` になった自動の再送でも新しいトークンを使うこと、エラーハンドラーの Retry でも同じ `x-request-id` を使うこと、`ResetSession` の後に `x-game-date` が付かず `authorization` が新しいトークンになること、`IDeviceService` に `x-app-platform`・`x-app-version` だけが付くこと、`Send` の外で本物のクライアントを呼ぶと `InvalidOperationException` になり送らないこと、`Send` の後に同じクライアントを呼んでも同じ例外になること(値が残らないこと)、`CorruptedAccountDataException` が `ACCOUNT_*` のまま届くこと

#### 44. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/VolTech.MagicOnion.Client.Tests.EditMode.asmdef` (Modify)

*   **Type / Scope**: ビルドの設定
*   **Proposed Changes**: 本物のクライアントを動かすのに要る参照を足す。この asmdef は `overrideReferences: true` なので、`precompiledReferences` に `MagicOnion.Client.dll`・`MagicOnion.Shared.dll`・`MagicOnion.Serialization.MessagePack.dll`・`MessagePack.dll` のようにファイル名で書く。正確な一覧は A3 で確かめる

#### Sandbox

#### 45. `Unity/VolTech.Sandbox/Assets/Smoke/Shared/ISmokeService.cs` (Modify)

*   **Type / Scope**: 確認用のコード
*   **Proposed Changes**: `IApiResponse<SmokeCommon>` を実装したレスポンスの型と、それを返すメソッドを足す。AOT で `TCommon` の判定とフォーマッターを確かめるため。`[SkipAuth]` のメソッドと `[NoSideEffects]` のメソッドも足す。生成時に読んだ値を確かめるため

#### 46. `Unity/VolTech.Sandbox/Assets/Smoke/AotCheck.cs` (Create)

*   **Type / Scope**: 確認用のコード(A6 で標準のプレイヤーテストが動いたら作らない)
*   **Proposed Changes**: サーバーが無くても動くよう、偽の `CallInvoker` で確かめる。パッケージのテストのコードは Sandbox から見えないので、ファイル 43 と同じ振る舞いの小さい偽物をこのファイルの中に持つ。起動時の入口は `[RuntimeInitializeOnLoadMethod]`。クライアントができたら、`[GenerateVoltechApi(typeof(ISmokeService))]` のラッパーを通し、`ApiCore`・`ApiHeaderFilter`・反映処理まで動かす。確かめること: フィルターの同期開始と `AsyncLocal` の読み取り、MessagePack の往復、`response is IApiResponse<TCommon>`、生成時に読んだ `[SkipAuth]`・`[NoSideEffects]` の値、複数の引数のタプル。項目ごとの結果をログに出し、全部の項目の後に `AOTCHECK PASS` か `AOTCHECK FAIL <項目>` を出す。プレイヤーでは、定義シンボル `VOLTECH_AOT_CHECK` があるときだけ起動時に動かし、`Application.Quit` の終了コードを成功 0、失敗 1 にする

#### 47. `Unity/VolTech.Sandbox/Assets/Smoke/Editor/AotCheckRunner.cs` (Create)

*   **Type / Scope**: 確認用のコード(A6 で標準のプレイヤーテストが動いたら作らない)
*   **Proposed Changes**: 2 つの入口を置く。`VolTech.Sandbox.Smoke.AotCheckRunner.RunInEditor` は Editor の中で `AotCheck` を動かし、`EditorApplication.Exit` の終了コードを成功 0、失敗 1 にする。`VolTech.Sandbox.Smoke.AotCheckRunner.BuildWindowsPlayer` は `VOLTECH_AOT_CHECK` を付けて Windows の IL2CPP でビルドする。出力先は `Unity/VolTech.Sandbox/Builds/AotCheck/VolTech.Sandbox.exe`、シーンは `EditorBuildSettings` のもの。スクリプティングバックエンドはスクリプトで IL2CPP にし、`finally` で元に戻す(`ProjectSettings` に差分を残さない)。ビルドの結果で `EditorApplication.Exit` の終了コードを成功 0、失敗 1 にする。どちらも `Unity -batchmode -projectPath <Sandbox のパス> -executeMethod <入口> -logFile <ログのパス>` で呼ぶ

### Verification Plan

*   **Compilation/Build**: `dotnet build VolTech-MagicOnion.slnx --configuration Release`。Unity は Sandbox(7000.0.0a12)でコンパイルが通り、パッケージのコードと生成コードに UAL0010 の警告が出ないこと
*   **Unit Tests**: `dotnet test VolTech-MagicOnion.slnx --configuration Release`(Docker が要る)。`unity test <Sandbox のパス> --mode EditMode`(Unity CLI。実装設計書のクライアントの「テスト」と同じ)
*   **Static Analysis & Linting**: `.editorconfig` のコードスタイルはビルドで確かめる。次の検索で 0 件になること: `Unity/com.chinpangx.voltech.magiconion/Runtime/Client` の `GetMethods`・`IsDefined`。`Unity/com.chinpangx.voltech.magiconion` と `docs/guide`・`docs/spec` の `CreateAll`・`CreateHeaders`・`CommonApplier`・`IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier`
*   **Test Coverage Requirement**: 数値の目標は置かない。下の表の項目を、表に書いた場所ですべて確かめる
*   **E2E/Integration**: Windows の IL2CPP のプレイヤーで `AotCheck` が全項目を通すこと。実サーバーを相手にした確認は、この設計書の完了条件に入れない(`docs/index.md` の既存の項目のまま)

| 確かめること | 確かめる場所 |
| --- | --- |
| CUJ 1: 生成した基底クラスで、トランザクション・冪等性・`Common` が効く | `GeneratedServiceTests` |
| CUJ 1: 対象外の形はコンパイルエラーになる | `GeneratorTests` |
| CUJ 2: ヘッダーが付き、呼び出し側に返る前に反映が終わる | `ApiHeaderFilterTests`、`CommonResponseTests` |
| CUJ 3: 再認証と、同じ `x-request-id` での再送 | `ApiHeaderFilterTests` |
| CUJ 4: 保存済みのレスポンスを読めないと `INTERNAL` になり、本体を呼ばない | `GeneratedServiceTests` |
| ヘッダーの名前・値・条件が変わっていない | `ApiHeaderFilterTests`、`ApiHeadersTests`(既存。変えない) |
| `ACCOUNT_*` の例外の型が保たれる | `ApiHeaderFilterTests` |
| サーバーとジェネレーターの属性の読み方が同じ | `RpcSettingsConsistencyTests` |
| コミットした DLL が今のソースと同じ動きをする | `CommittedDllTests` |
| 生成コードが実物の基盤の型とコンパイルできる | EditMode のコンパイル(`TestApi`) |
| `TCommon` の判定、フォーマッター、`AsyncLocal` の読み取り(Windows IL2CPP) | `AotCheck` |

*   **Windows の IL2CPP の手順**: `BuildWindowsPlayer` でビルドし、`Unity/VolTech.Sandbox/Builds/AotCheck/VolTech.Sandbox.exe` を `-batchmode -nographics -logFile <ログのパス>` で起動する。終了コードが 0 で、ログの中に `AOTCHECK PASS` があれば成功。結果を日付と方法を添えて `docs/index.md` に書く
*   **iOS の AOT**: Mac が要るので、この設計書の完了条件に入れない。同じ `AotCheck` を iOS でビルドして確かめる項目を `docs/index.md` に残す(RFC の未確認の項目)
*   **実装の後**: `/egm:egm-drift-report` で設計書とコードのずれを確かめ、ずれは設計書に戻す

--------------------------------------------------------------------------------

*Drafted with EGM*
