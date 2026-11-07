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

ただし、フィルターだけでは移しきれない。2026-11-04 に、MagicOnion.Client 7.11.0 を .NET 10 の動的クライアントで動かして確かめた(スクラッチのプログラム。リポジトリには残していない)。Unity のソースジェネレーターのクライアントでも同じかは未確認。

- フィルターの中で `context.CallOptions.Headers` は null ではなく、足したヘッダーは送信に載る。呼び出しごとに新しい `Metadata` になる(前の呼び出しのヘッダーは残らない)
- `MagicOnionClient.Create<T>(callInvoker, filters)` で渡したフィルターは、`WithOptions` の後も動く。順番は配列の先頭が外側
- `RequestContext.CallOptions` は読み取り専用(`CallOptions` は struct で、置き換えられない)。**締め切りとキャンセルは、フィルターからは変えられない**。`WithOptions` で渡す必要が残る
- `RequestContext.Items` は呼び出しごとに MagicOnion が空で作る。呼ぶ側から値を入れておく口は無い
- `WithOptions` で渡したヘッダーは、フィルターの `context.CallOptions.Headers` で読める
- 先頭のフィルターの `SendAsync` は、RPC のメソッドを呼んだ時点で、同じスレッドで同期的に始まる(`await` の前に入る)。`AsyncLocal` に入れた値は、待機をまたがずに読める(2026-11-07、同じ方法で確かめた。IL2CPP でも同じに動く前提で設計し、実機の確認は Verification Plan に入れる)

そのため、呼び出しごとの値を `ApiCore` とフィルターの間でどう受け渡すかを決める必要がある。`x-request-id` は再送で同じ値を使う。`authorization` は RPC が認証を要るかを知る必要があり、弾かれたトークンを `ApiCore` が `tokenProvider.Invalidate` に渡すので、使った値を `ApiCore` へ返す必要もある。

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
*   **Q2(解決)**: 保存済みのレスポンスを今の型として読めないときは、例外のまま `INTERNAL` にする。コードは足さず、動きを仕様に書く。処理はコミット済みなので、再送は成功させられない。タイトルに戻れば、サインインで全件を取り直せる。値がずれて読めた場合は検出しない(2026-11-07)
*   **Q3(解決)**: クライアントの反映処理は、`TCommon` を受け取る 1 つにまとめる。player_diff → バッジ → 通知の順は、アプリが守る取り決めにする。VolTech-gRPC 版と名前はそろわなくなる(2026-11-07)
*   **Q4(解決)**: ヘッダーは全部 `IClientFilter` に移す。呼び出しごとの値(`x-request-id`・認証の要否・使ったトークン)は、`AsyncLocal` の入れ物で `ApiCore` とフィルターが共有する。締め切りとキャンセルは `WithOptions` に残る(2026-11-07)
*   **Q5(解決)**: アプリは `IClientFilter` を足せない。フィルターは基盤のものだけで、基盤のヘッダーのフィルターが必ず先頭になる。Monitor(設計書 4)も基盤の内部で足す。必要になったら、基盤より内側に足す口を作る(2026-11-07)
*   **Q6(解決)**: ジェネレーターの対象は、アプリが `partial class` に `typeof(共有インターフェース)` を書いた属性で明示する。MagicOnion の `[MagicOnionClientGeneration]` と同じ形(2026-11-07)
*   **Q6b(解決)**: 生成する基底クラスは、全 RPC で `RunAsync` を通す。外す属性は足さない。DB を使わない RPC が必要になったら、そのサービスだけ `ServiceBase<T>` を手書きにする(2026-11-07)
*   **Q7(解決)**: ソースジェネレーターは Unity 7 で動く。Uta の `client/uta` では、MagicOnion・MessagePack・MasterMemory のジェネレーターが `RoslynAnalyzer` ラベルの DLL として動いている。基盤も同じ形で配る(2026-11-07)

## Critical User Journeys (CUJs)

### CUJ 1: サーバーの RPC を足す

*   **Given**: アプリが共有インターフェース `IGachaService` を持ち、`IApiResponse<AppCommon>` のレスポンス型を返す
*   **When**: `[GenerateVoltechService(typeof(IGachaService))] public abstract partial class GachaServiceBase;` を書き、`GachaService : GachaServiceBase` で `DrawAsync(TxContext tx, DrawRequest request)` を実装する
*   **Then**: `RunAsync` を手で呼ばなくても、トランザクション・冪等性・`Common` が必ず効く。実装しなければコンパイルが通らない。外で詰め替える書き方はできない

### CUJ 2: クライアントから RPC を呼び、反映する

*   **Given**: アプリが `ApiCoreBuilder` に反映処理(`ICommonResponseApplier<AppCommon>`)を 1 つ渡している
*   **When**: 生成された `GachaApi.DrawAsync(request)` を呼ぶ
*   **Then**: 基盤のフィルターがヘッダーを付けて送る。成功したら、呼び出し側に返る前に反映が終わっている

### CUJ 3: トークンが切れていて、再認証する

*   **Given**: 持っているトークンがサーバーで無効になっている
*   **When**: 認証が要る RPC を呼ぶ
*   **Then**: サーバーが `UNAUTHENTICATED` を返す。`ApiCore` はフィルターが使ったトークンを `Invalidate` に渡し、同じ `x-request-id` で 1 回だけ送り直す。動きは今と同じ

### CUJ 4: デプロイをまたいで再送する

*   **Given**: 保存済みのレスポンスを、新しい型として読めない
*   **When**: クライアントが同じ `x-request-id` で再送する
*   **Then**: サーバーは `INTERNAL` を返す。処理はコミット済みなので再実行しない。タイトルに戻ってサインインすれば、全件を取り直せる

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
*   **配り方**: サーバーは `ProjectReference` に `OutputItemType="Analyzer"` を付けて使う。Unity は、ビルドした DLL を `Unity/com.chinpangx.voltech.magiconion/Analyzers/` に置いてコミットする。`.meta` に `RoslynAnalyzer` ラベルを付け、全プラットフォームを無効にする。DLL はプロジェクトのビルド後の処理で写し、手で写さない
*   **対象の指定**: `partial class` に属性を付ける。属性は参照先のアセンブリの型も `typeof` で指定できる
*   **エラー**: 次はコンパイルエラーにする。`UnaryResult<T>` 以外を返すメソッド(戻り値なしの `UnaryResult` と StreamingHub は対象外)、`partial` でないクラス、`IService<T>` でない型

#### サーバーの生成物

`[GenerateVoltechService(typeof(IGachaService))] public abstract partial class GachaServiceBase;` から、次を生成する(形の例で、実コードではない)。

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
*   **迂回の防止**: `ServiceBase<T>` を直接継承すれば `RunAsync` を迂回できる。依存の向きのテストの例を利用ガイドに書き、アプリのテストで止める。基盤の `DeviceService` は手書きのまま

#### クライアントの生成物

`[GenerateVoltechApi(typeof(IGachaService))] public sealed partial class GachaApi;` から、次を生成する。

```text
sealed partial class GachaApi : ApiServiceBase<IGachaService>
  public GachaApi(ApiCore core)                      // core が作ったクライアントを使う
  public GachaApi(ApiCore core, IGachaService client) // テスト用の偽物を渡す
  public UniTask<ApiResult<DrawResponse>> DrawAsync(
      DrawRequest request, IEnumerable<ErrorCode> handledCodes = null, bool notifyActivity = true)
```

*   `[SkipAuth]`・`[NoSideEffects]` は生成時に読み、`RpcSettings` の値を生成コードに埋め込む。`RpcSettings.CreateAll` のリフレクションと、`docs/index.md` の未確認の項目(属性を IL2CPP で読めるか)は無くなる
*   引数が複数の RPC は、今と同じくタプルにまとめて `InvokeAsync` に渡す
*   手書きのラッパーの形(`ApiServiceBase` を継承して `InvokeAsync` を呼ぶ)は、基盤の公開 API から外す

### 2. クライアントのヘッダー付与(`IClientFilter`)

*   **クライアントを作る場所**: アプリは `CallInvoker`・シリアライザー・`IMagicOnionClientFactoryProvider`(アプリごとの生成物)を `ApiCoreBuilder` に 1 回渡す。`ApiCore` が基盤のフィルターを付けて `MagicOnionClient.Create<T>` を呼ぶ。これでフィルターの渡し忘れが無くなる
*   **呼び出しごとの入れ物**: `ApiCallContext`(内部の型)が `RequestId`・`RequiresAuth`(入力)と `UsedToken`(出力)を持つ。`ApiCore` が送る直前に `AsyncLocal` に入れ、`call(...)` の直後に外す
*   **読む順番**: `ApiHeaderFilter` は `SendAsync` の入り口、最初の `await` の前に入れ物の参照を取る。確かめたとおり、ここは `call(...)` の中で同期的に動くので、UniTask の待機をまたがない
*   **付けるヘッダー**: `authorization`(認証が要るときだけ)、`x-request-id`(冪等性の対象だけ)、アプリのバージョン、`x-game-date`(持っているときだけ)。名前・値・条件は今の `CreateHeaders` と同じ
*   **トークン**: フィルターが `GetTokenAsync` を待ち、使った値を `UsedToken` に書く。`ApiCore` は再認証のときにそれを `Invalidate` に渡す。トークンを取る途中の失敗は `UsedToken` が null のままなので、今と同じく何も捨てない
*   **`WithOptions` に残るもの**: 締め切りとセッションのキャンセルだけ
*   **`IDeviceService`**: `DeviceTokenProvider` が直接呼ぶ今の形のまま。フィルターは通さない。トークンを取るためのトークンが要らないため
*   **テストの分け方**: ヘッダーを作る処理を内部の `ApiHeaderWriter` に置き、`ApiHeaderFilter` は薄くする。偽のクライアントはフィルターを通らないので、`ApiCoreTests` の偽物が `ApiHeaderWriter` を呼び、今のテストと同じヘッダーを確かめる

### 3. 共通レスポンス項目の反映

*   `IPlayerDiffApplier<TCommon>`・`IBadgeApplier<TCommon>`・`INotificationApplier<TCommon>` を消し、`ICommonResponseApplier<TCommon>`(`UniTask ApplyAsync(TCommon common)`)を 1 つ置く
*   `ApiCoreBuilder.WithCommonResponseApplier<TCommon>(applier)` で渡す。省略できる
*   `Build` での `TCommon` の食い違いの検査は消す。1 つしか渡せないので起きない
*   `CommonApplier` は、`Name` と `CommonType` を消し、「レスポンスから `Common` を取り出して渡す」だけの内部の部品にする
*   反映の位置と例外の扱いは今と同じ: レスポンスヘッダーを反映した後、順番待ちの中で呼ぶ。古いセッションでは呼ばない。例外は `CLIENT_ERROR` にして共通エラーハンドラーに渡し、送り直さない
*   ログの文言から「どの反映処理か」は無くなる。スタックトレースで分かる
*   player_diff → バッジ → 通知の順は、仕様の「項目ごとの決めごと」(アプリが守る取り決め)に移す

### 4. 保存済みのレスポンス

コードは変えない。読めないときは `Deserialize` の例外が `RpcLogFilter` で `INTERNAL` になる。仕様に次を書く: 再送は失敗する。処理はコミット済みなので再実行しない。タイトルに戻れば、サインインで取り直せる。型のキーの意味を変えたデプロイの前後では起こりうる。値がずれて読めた場合は検出しない。

### 5. AOT(IL2CPP)で動く前提

*   `TCommon` は `class` に限る(参照型なのでコードを共有できる)
*   `response is IApiResponse<TCommon>` は、インターフェースの型の判定で、実行時のコード生成を使わない
*   フォーマッターは、具体的なレスポンスの型ごとに MessagePack のジェネレーターが作る。`IApiResponse<>` そのもののフォーマッターは要らない
*   クライアントの実行時のリフレクションは、ラッパーの生成で無くなる
*   上の 3 つと `AsyncLocal` の読み取りは、Windows の IL2CPP のプレイヤーで確かめる(Verification Plan)

### 変えないもの

ヘッダーの名前と値、ErrorInfo、予約エラー、冪等性の意味、`Common` が null なら変化なしという意味、再送で同じ `Common` を返すこと、`ICommonResponseCollector<TCommon>`・`AddCommonResponse`・`TxContext.GetCollector`。

--------------------------------------------------------------------------------

*Drafted with EGM*
