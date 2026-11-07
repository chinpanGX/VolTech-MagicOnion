# 共通レスポンス項目とクライアントの呼び方を、MagicOnion の公式の書き方に合わせて見直す

*   **Author:** ちんぱん
*   **Status:** Approved
*   **Date:** 2026-11-07

--------------------------------------------------------------------------------

## Overview

[基盤の方針と進める順番](foundation-direction.md)(RFC)の「進める順番」3 番(設計書 1)。次の 3 つを 1 回の変更として設計する。

1. **共通レスポンス項目**(player_diff・バッジ・通知)。`main` に入っている実装(`feature/common-response` で入れたもの)を、RFC の Analysis 5 の指摘に答える形で直す
2. **クライアントの呼び方**。アプリが共有インターフェース(`IGachaService`)を、MagicOnion の公式の書き方で直接呼ぶ。`ApiCore` の処理は `IClientFilter` から動かし、ヘッダーと締め切りは基盤が包む `CallInvoker` で付ける。手書きのラッパーと `ApiServiceBase` はやめる
3. **サーバーの書き方**。公式と同じ `ServiceBase<T>` の手書きのままにする。`Common` の抜けは、実行時の検査で見つける

コード生成は作らない。2026-11-07 にオーナーが、サーバーとクライアントのアプリの書き方を公式に合わせると決めた。前の版で承認した、基底クラスとラッパーの生成は取り下げた(Alternative F)。

1 本にまとめる理由は、1 と 2 がどちらも基盤の公開 API の組み立て方を変えるから。別々に出すと、Uta(この基盤を使う個人開発のゲーム)が組み立ての処理を何度も移すことになる。

RFC の「進める順番」の 1・2 番(YetAnotherHttpHandler でトレーラーが届くかの確認、取り決めの固定)は、この設計の前提にしない。この設計はエラーの形とクライアントとの取り決めを変えないので、結果に左右されない。

クライアントとの取り決め(ステータスと ErrorInfo、ヘッダー名と値、予約エラー、冪等性の意味)は変えない(RFC の「まだ決めていないこと」の 2。解決済み)。この設計書で変えるのは、基盤の公開 API、内部の作り、`Common` の検査(今まで成功していた誤った形が `INTERNAL` になる)だけ。Uta はリリース前なので、基盤の公開 API の破壊的な変更は許す(同じく 3)。

この設計書が承認されたら、`docs/designs/implementation/client.md` の該当の節をこの設計書へのリンクに置き換える。`docs/designs/implementation/server.md` の「共通レスポンス項目」は、コレクターの説明をこの設計書が変えないので残し、リンクを足す。振る舞いとクライアントとの取り決めは `docs/spec/` に残し、変わる節を直す。CLAUDE.md の規則では重なる節をリンクに置き換えるが、クライアントとの取り決めは `docs/spec/` だけを読めば分かるようにしたいので、ここは書き直して残す。食い違ったら、CLAUDE.md の順のとおり設計書が正しい。

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

### クライアントの呼び方

今のアプリは、共有インターフェースごとにラッパー(`ApiServiceBase<T>` の派生)を手で書き、画面からはラッパーを呼ぶ。MagicOnion の公式の書き方では、アプリは共有インターフェースをそのまま呼び、呼び出しに共通する処理は `IClientFilter` に書く。

| | 公式の書き方 | 今のコード |
| --- | --- | --- |
| 呼び方 | `await client.DrawAsync(request)` | `await gachaApi.DrawAsync(request)`(手書きのラッパー) |
| 共通の処理 | `IClientFilter` | `ApiCore` |
| 呼び出しごとの値 | フィルターで付ける | `ApiCore` が `WithOptions(callOptions)` で渡す |

ただし、公式の呼び方だけでは足りないものが 3 つある。

- **締め切りとキャンセル**: `RequestContext.CallOptions` は読み取り専用で、フィルターからは変えられない。セッションを終えるときに通信を止める仕組みも、試行ごとの締め切りも、フィルターでは作れない
- **呼び出しごとのヘッダー**: `Metadata` はクライアントごとに使い回されるので、フィルターで書くと同時の呼び出しで混ざる(下の確認結果)
- **失敗の受け取り方**: `await client.DrawAsync(request)` の結果はレスポンスそのもので、失敗は例外でしか届かない。アプリは `try/catch` を書かず、`IsError`・エラーコード・エラーメッセージで処理したい(2026-11-07 にオーナーが決定)

2026-11-07 に、MagicOnion.Client 7.11.0 を .NET 10 の動的クライアントと偽の `CallInvoker` で動かして確かめた(スクラッチのプログラム。リポジトリには残していない)。ソースジェネレーターのクライアントでも同じことを、`ClientBehaviorTests` で確かめた(「実装の前に確かめた項目」)。

- フィルターの中で `await` してから `next` を呼べる。`next` を複数回呼べるので、再送をフィルターで書ける
- `context.CallOptions.Headers` の `Metadata` は、クライアントごとに 1 つを使い回す。呼び出しごとの写しではない。フィルターで書き換えると、同じクライアントの次の呼び出しや同時の呼び出しに残る。`WithOptions` で毎回新しい `Metadata` を渡していた今のコードでは、見えなかった動き
- `next(context)` を呼ぶと、その呼び出しの中で `CallInvoker` まで進む。`await` する前の `ValueTask` を受け取った時点で、`CallInvoker` は呼ばれ終わっている
- 失敗は `await next(context)` が `RpcException` を投げる。成功なら `ResponseContext` が返り、`ResponseHeadersAsync` でレスポンスヘッダーを読める
- `context.MethodPath` は `IGachaService/DrawAsync` の形。`ResponseType` は、`UnaryResult<int>` の RPC では `Int32`、戻り値なしの RPC では `Nil` だった
- `CallInvoker` を包むと、試行ごとにヘッダー・締め切り・キャンセルを付けられる。フィルターの後で動くので、トークンを待つ時間は締め切りを消費しない
- フィルターが投げた例外は、型を保ったまま呼び出し側に届く
- `WithHeaders` を 2 回つなぐと、後の指定だけが残る
- `GetResponseAs<object>()` は例外にならず、null を返す。フィルターは型を知らないので、レスポンスを読めない
- `CallInvoker` の `AsyncUnaryCall<TRequest, TResponse>` の `TResponse` は、参照型のレスポンスならその型。値型のレスポンスと戻り値なしの RPC では、MagicOnion の内部の `Box<T>`

## Goals

*   アプリの書き方を MagicOnion の公式に合わせる。サーバーは `ServiceBase<T>` を継承し、クライアントは共有インターフェースを直接呼ぶ。失敗を受け取るために `ToResult()` だけを足す
*   `IApiResponse<TCommon>` を実装したレスポンスが `RunAsync` を通らずに返ったら、実行時に検出して `INTERNAL` にする
*   保存済みのレスポンスを今の型として読めないときの、再送の振る舞いを決め、仕様に書く
*   クライアントの共通レスポンス項目の反映の公開 API を、基盤が `TCommon` の中身を知らない前提に合う最小の形にする
*   順番待ち・再認証・エラーハンドラーを `IClientFilter` から動かし、ヘッダー・締め切り・セッションのキャンセルは包んだ `CallInvoker` で付ける。ヘッダーの名前・値・付く条件、再送で同じ `x-request-id` を使うことは変えない
*   アプリは `try/catch` を書かず、`IsError`・`ErrorCode`・`ErrorMessage` で失敗を処理する。エラーの処理は共通エラーハンドラーが行い、アプリは各 API でダイアログを書かない
*   AOT(IL2CPP)で動く前提(使ってよいリフレクション・ジェネリック)を書く。IL2CPP での確認はしない(Q8)
*   変えた振る舞いを、サーバー(`tests/VolTech.Server.Tests`)と EditMode(`Tests/EditMode`)のテストで確かめる
*   Unity のクライアントが本物のサーバーと通信できることを、PlayMode のテストで確かめる。ヘッダー、認証、再認証、再送、エラーの形、`Common`、キャンセル、締め切りを、実際の通信で確かめる

## Non-Goals

*   `TCommon` の中身(player_diff・バッジ・通知の型)を基盤で決めること。アプリが決める
*   クライアントとの取り決めを変えること(ヘッダー名、ErrorInfo、予約エラー、冪等性の意味)
*   反映処理の登録忘れを、起動時に調べる仕組み(RFC の Non-Goals。VolTech-gRPC 版にも無い)
*   コード生成。サーバーの基底クラスも、クライアントのラッパーも生成しない
*   Monitor・Tracing(設計書 4)。記録の場所は設計書 4 で決める。`IDeviceService` は `ApiCallFilter` を通らないので、トークンの発行も記録するなら、両方が使う `callInvoker` を `ApiChannel.CreateCallInvoker` で包む形が候補になる
*   ログと計測(設計書 2)。クライアントのログ(`IApiLogger`)の仕組みは変えない
*   `last_responses` の保存形式(MessagePack のバイト列)を変えること
*   アプリが `IClientFilter` を足せるようにすること
*   IL2CPP と iOS の AOT で確かめること(Q8)
*   StreamingHub

## Open Questions

未解決の項目は無い。実装の前に確かめた結果は「実装の前に確かめた項目」に書く。

### 解決済み

番号は Alternatives Considered から参照するので残す。欠けている番号は、コード生成とともに取り下げた項目。

*   **Q1(解決)**: `Common` が黙って空になる問題は、基盤のフィルターで実行時に検査し、`INTERNAL` にする。コード生成で防ぐ前の決定は、公式の書き方に合わせるために取り下げた(2026-11-07)
*   **Q2(解決)**: 保存済みのレスポンスを今の型として読めないときは、例外のまま `INTERNAL` にする。コードは足さず、動きを仕様に書く。処理はコミット済みなので、再送は成功させられない。タイトルに戻れば、サインインで全件を取り直せる。その応答の通知は失われる。値がずれて読めた場合は検出しない(2026-11-07)
*   **Q3(解決)**: クライアントの反映処理は、`TCommon` を受け取る 1 つにまとめる。player_diff → バッジ → 通知の順は、アプリが守る決めごとにする。VolTech-gRPC 版と名前はそろわなくなる(2026-11-07)
*   **Q4(解決)**: 呼び出しに共通する処理は `IClientFilter` から動かす。フィルターでは扱えないヘッダー・締め切り・キャンセルは、基盤が包む `CallInvoker` で付ける(2026-11-07)
*   **Q5(解決)**: アプリは `IClientFilter` を足せない。フィルターは基盤のものだけ。Monitor(設計書 4)も基盤の内部で足す。必要になったら、基盤より内側に足す口を作る(2026-11-07)
*   **Q8(解決)**: IL2CPP の確認が前提になっている箇所は、IL2CPP の確認不要として実装する。Windows の IL2CPP と iOS の AOT での確認は、この設計書の作業に含めない(2026-11-07)
*   **Q9(解決)**: エラーは共通エラーハンドラーがフィルターの中で処理する。アプリは `ToResult()` で `IsError`・`ErrorCode`・`ErrorMessage` を受け取り、`try/catch` を書かない。自分で処理するコードは `WithHandledCodes(...)` で指定する(2026-11-07)

## Critical User Journeys (CUJs)

### CUJ 1: サーバーの RPC を足す

*   **Given**: アプリが共有インターフェース `IGachaService` を持ち、`IApiResponse<AppCommon>` のレスポンス型を返す
*   **When**: `GachaService : ServiceBase<IGachaService>, IGachaService` を書き、`DrawAsync` の中で `ITransactions.RunAsync` を呼ぶ
*   **Then**: トランザクション・冪等性・`Common` が効く。`RunAsync` を通らずに返すと、検査のフィルターが `INTERNAL` にする

### CUJ 2: クライアントから RPC を呼び、反映する

*   **Given**: アプリが `ApiCoreBuilder` に反映処理(`ICommonResponseApplier<AppCommon>`)を 1 つ渡している
*   **When**: `await gachaClient.DrawAsync(request).ToResult()` を呼ぶ
*   **Then**: 基盤がヘッダーを付けて送る。成功したら、呼び出し側に返る前に反映が終わっている。`result.IsError` は false

### CUJ 3: トークンが切れていて、再認証する

*   **Given**: 持っているトークンがサーバーで無効になっている
*   **When**: 認証が要る RPC を呼ぶ
*   **Then**: サーバーが `UNAUTHENTICATED` を返す。フィルターは使ったトークンを `Invalidate` に渡し、同じ `x-request-id` で 1 回だけ送り直す。動きは今と同じ

### CUJ 4: 業務エラーを受け取る

*   **Given**: サーバーが業務エラーを返す
*   **When**: `ToResult()` だけを付けて呼ぶ。または `WithHandledCodes(GachaErrors.NotEnoughGems)` を付けて呼ぶ
*   **Then**: `ToResult()` だけなら、共通エラーハンドラーがダイアログを出した後、`IsError` が true の結果が返る。`WithHandledCodes` で指定したコードなら、ダイアログを出さずに `IsError` が true の結果が返る。予約エラーは、指定しても共通エラーハンドラーに回る(今と同じ)。どちらも呼び出し側に `try/catch` は要らない

### CUJ 5: デプロイをまたいで再送する

*   **Given**: 保存済みのレスポンスを、新しい型として読めない
*   **When**: クライアントが同じ `x-request-id` で再送する
*   **Then**: サーバーは `INTERNAL` を返す。処理はコミット済みなので再実行しない。`INTERNAL_ERROR` の既定は Retry だが、送り直しても同じ `x-request-id` で必ず失敗する。タイトルに戻ってサインインすれば全件を取り直せる。その応答の通知は失われる

## Technical Plan (Architecture)

### 全体の流れ

```text
アプリ: await gachaClient.WithHandledCodes(...).DrawAsync(request).ToResult()
  → ApiCallFilter(IClientFilter)→ ApiCore の送信の処理
      順番待ち → トークンを取る → ApiCallContext を入れる
      → ApiCallInvoker(試行ごとのヘッダー・締め切り・キャンセル)→ 通信
                                                 フィルター(ログ → 時刻 → 認証 → 日付 → 検査)
                                                 GachaService.DrawAsync
                                                   → ITransactions.RunAsync
                                                     → 本体 → Common を詰める → 保存 → コミット
      ← レスポンス
      → 世代の確認 → レスポンスヘッダーの反映 → ICommonResponseApplier
      失敗なら: 再認証 → 共通エラーハンドラー → ApiException
  ← ToResult が ApiResult<T> にする
```

### 1. アプリの書き方

#### 組み立て

```text
var deviceClient = MagicOnionClient.Create<IDeviceService>(callInvoker, serializerProvider, Array.Empty<IClientFilter>(), clientFactoryProvider);
var tokenProvider = new DeviceTokenProvider(deviceClient, accountStore, options);
var core = ApiCore.CreateBuilder(
        callInvoker, serializerProvider, clientFactoryProvider, options,
        tokenProvider, errorPolicy, errorHandler)
    .WithCommonResponseApplier(applier)
    .Build();
var gachaClient = core.CreateClient<IGachaService>();
```

*   `callInvoker` は `ApiChannel`、`serializerProvider` と `clientFactoryProvider` はアプリの `[MagicOnionClientGeneration]` の生成物(今の利用ガイドと同じ)
*   `IDeviceService` のクライアントと `DeviceTokenProvider` は、今のまま作る。`ApiCore` を通さず、公式の `MagicOnionClient.Create<T>` で作ったクライアントを使う
*   `core.CreateClient<T>()` は、基盤のフィルターと包んだ `CallInvoker` を付けて `MagicOnionClient.Create<T>` を呼ぶ。戻り値は `T` そのもの
*   アプリは `[MagicOnionClientGeneration]` に、アプリの共有インターフェースのアセンブリの型と、`IDeviceService` を書く。今までと同じ。書いた型のアセンブリ全体が生成の対象になる。`IDeviceService` は `Abstractions` のアセンブリにあるので、別に書く。書き忘れはコンパイルでは分からず、クライアントを作るときに `NotSupportedException` になる

#### 呼び方

```text
// 普通の呼び方。エラーは共通エラーハンドラーが処理する
var result = await gachaClient.DrawAsync(request).ToResult();
if (result.IsError) { return; }
Show(result.Value);

// 自分で処理するコードを指定する
var result = await gachaClient
    .WithHandledCodes(GachaErrors.NotEnoughGems)
    .DrawAsync(request)
    .ToResult();
if (result.IsError && result.ErrorCode == GachaErrors.NotEnoughGems) { ShowShop(); }

// 進行中の表示を出さずに呼ぶ。エラーは共通エラーハンドラーが処理する
var result = await gachaClient.WithoutActivity().DrawAsync(request).ToResult();
```

*   `ToResult()` は `UnaryResult<T>` の拡張メソッド。`ApiException` を受け止めて `ApiResult<T>` にする
*   `ToResult()` は `UnaryResult<T>` だけに足す。戻り値なしの `UnaryResult` の RPC には今と同じく対応しない。レスポンスの型を返すように書く
*   `ApiResult<T>` は `IsError`・`ErrorCode`・`ErrorMessage`・`Error`・`Value` を持つ。`ErrorMessage` はログ用で、ユーザーには見せない(今の `ApiError.Message` と同じ)。`Value` を失敗のときに読むと例外になる(今と同じ)
*   `WithoutActivity` が変えるのは進行中の通知だけで、エラーは共通エラーハンドラーが処理する
*   `WithHandledCodes` は `WithOptions` と同じく、RPC を呼ぶ前につなぐ。フィルターは RPC を呼んだ時点で動くので、後ろからでは間に合わない
*   `WithHeaders` は前の指定を消すので、基盤のチェーンは 1 回で済む形にする。`WithoutActivity(params ErrorCode[] codes)` は、裏で呼びながらコードも指定できる。`WithHandledCodes` と `WithoutActivity` を続けてつながない。アプリの `WithHeaders`・`WithOptions` も、これらの指定を消すので使わない

### 2. クライアントの処理の移し先

| 今の `ApiCore` の処理 | 移し先 |
| --- | --- |
| 順番待ち、トークンの取得、再認証、Retry、世代の確認、反映、共通エラーハンドラー、画面の遷移、進行中の通知 | `ApiCore` に残す。`ApiCallFilter`(`IClientFilter`)から呼ぶ |
| ヘッダーの組み立て、締め切り、セッションのキャンセル | `ApiCallInvoker`(`CallInvoker` を包む) |
| 状態: セッション(世代・キャンセル・ゲーム内の日付)、`ServerClock`、順番待ち(`requestQueue`・`errorHandlerQueue`)、進行中の数 | `ApiCore` に残す。全サービスで 1 つにするため |

*   **フィルターの役目**: `ApiCallFilter` は `CreateClient<T>` ごとに作り、持つのは `RpcSettings` の表だけ。`MethodPath` で設定を引き、`x-voltech-call` を読み、`ApiCore` の internal メソッド `SendAsync(settings, context, next, handledCodes, notifyActivity)` に渡すだけにする。順番待ちと進行中の数をサービスごとに分けると、別のサービスの冪等性の対象の RPC が同時に走り、再送で二重実行になるので、処理と状態は `ApiCore` に 1 つだけ置く
*   **`ApiCore` の処理**: 今の `InvokeAsync` → `SendInQueueAsync` → `SendWithRetryAsync` → `SendOnceAsync` → `CompleteAsync` の順と分岐を保つ。引数の `client`・`call` を `context`・`next` に換え、戻り値を `ApiResult<T>` から `ResponseContext` に変える。`call(...)` と `rpc.ResponseAsync` の代わりに `await next(context)` を呼び、`rpc.ResponseHeadersAsync` の代わりに `response.ResponseHeadersAsync` を読む。失敗は `await next(context)` が投げる `RpcException` を受ける
*   **成功**: `next` が返した `ResponseContext` をそのまま返す。呼び出し側の `await` にレスポンスが届く
*   **失敗**: 共通エラーハンドラーと画面の遷移を済ませた後、`ApiException` を投げ、`ToResult` が受け止める。gRPC のクライアントが失敗を `RpcException` で返すので、基盤の中の例外はこれより減らせない。失敗のときだけのコストで、成功のときには掛からない
*   **捨てた呼び出し**: 古いセッションの呼び出しも `ApiError.Discarded()` の `ApiException` にする。`result.Error.IsDiscarded` で分かる
*   **RPC ごとの設定**: 今の `RpcSettings.CreateAll` のリフレクションを、`CreateClient<T>` のときに 1 回行う。設定は `context.MethodPath` のメソッド名で引く。`context.MethodPath` の `/` より後を使う。基底インターフェースから受け継いだメソッドは、MagicOnion のソースジェネレーターが生成せず、アプリのコンパイルエラーになる
*   **ヘッダー**: `ApiCore` もフィルターも `Metadata` を書き換えない(クライアントごとに使い回されるため)。`ApiCallInvoker` が試行ごとに新しい `Metadata` を作り、次のヘッダーだけを入れる。`CallOptions.Headers` の中身は写さない(今の `WithOptions` と同じく丸ごと置き換える): `authorization: Bearer <token>`(トークンがあるときだけ)、`x-request-id`(冪等性の対象だけ)、`x-app-platform`・`x-app-version`(`ApiCoreOptions.AppVersion` が null でないときだけ)、`x-game-date`(持っているときだけ)。名前・値・条件は今の `ApiCore.CreateHeaders` と同じ
*   **呼び出しごとの値**: `ApiCore` は試行ごとに `ApiCallContext`(`Token`・`RequestId`・`GameDate`・`CancellationToken`)を作り、`AsyncLocal` に入れて `next(context)` を呼び、`ValueTask` を受け取った直後(`await` の前)に null に戻す。`ApiCallInvoker` はその間に読む。`await` をまたいだ引き継ぎには頼らない。`ApiCallContext` そのものが null なら例外にする(黙ってヘッダーなしで送らない)
*   **セッションのキャンセル**: 呼び出しの開始時に読んだ世代と `CancellationToken` を、再送を含めて最後まで使う(今と同じ)。順番待ちや認証トークンの取得の間にセッションが変わると、`CancellationToken` はキャンセル済みなので、古い呼び出しは通信の前に止まる
*   **チェーンの指定**: `WithHandledCodes`・`WithoutActivity` は、内部のヘッダー `x-voltech-call` に値を入れて `WithHeaders` で渡す。`WithHandledCodes` は進行中を通知する設定で、`WithoutActivity` は通知しない設定で渡す。フィルターは読むだけで、`ApiCallInvoker` は新しい `Metadata` に入れないので、`core.CreateClient<T>()` で作ったクライアントではサーバーに届かない。`CallOptions.Headers` が null なら、指定なしと同じに扱う。`Metadata` を書き換えないので、指定したクライアントを変数に持って何度呼んでも効く
*   **レスポンスの読み取り**: `ApiCallInvoker` は `TResponse` を知っているので、試行の `ApiCallContext.ReadResponse`(`Func<object>`)に、内側の `call.ResponseAsync.Result` を読むデリゲートを入れる。`ApiCore` は `await next(context)` が返った後、反映処理があるときだけ読む。リフレクションは要らない。値型と戻り値なしの RPC は `Box<T>` が読めるが、`IApiResponse<>` を実装しないので反映されない。読み取りの例外は反映の `try` の中で扱い、`CLIENT_ERROR` にして送り直さない
*   **認証トークン**: `GetTokenAsync` はフィルターの中で待つ。`ApiCallInvoker` はその後に締め切りを計算するので、今と同じくトークンを待つ時間は締め切りを消費しない
*   **`IDeviceService`**: `ApiCallFilter` を通さない。トークンの取得から同じフィルターに戻らないようにするため。`DeviceTokenProvider` は今のまま、`WithOptions` でアプリのバージョンのヘッダーと締め切りを渡す
*   **アプリのテスト**: `IGachaService` を実装した偽物をそのまま使える。公式の書き方と同じ。失敗を返すときは、`FakeRpcErrors.ApiFailureResult<T>` で作った `UnaryResult<T>` を偽物から返す。`ToResult` が失敗の結果にする。偽物のメソッドから同期で例外を投げると、`ToResult` に届かない。`FakeRpcErrors.FailedPrecondition` などの `RpcException` も、`ToResult` は受け止めない

### 3. 共通レスポンス項目の反映

*   `IPlayerDiffApplier<TCommon>`・`IBadgeApplier<TCommon>`・`INotificationApplier<TCommon>` を消し、`ICommonResponseApplier<TCommon>`(`UniTask ApplyAsync(TCommon common)`)を 1 つ置く
*   `ApiCoreBuilder.WithCommonResponseApplier<TCommon>(applier)` で渡す。省略できる
*   `Build` での `TCommon` の食い違いの検査は消す。反映処理は 1 つしか渡せないので、反映処理どうしの食い違いは起きない。反映処理とレスポンスの `TCommon` の食い違いは、今と同じく検出せず、反映されない。1 つのアプリの `TCommon` は 1 つなので、これを受け入れる
*   `CommonApplier` は消す。`WithCommonResponseApplier` の中のクロージャーが、レスポンスから `Common` を取り出して渡す
*   反映の位置と例外の扱いは今と同じ: レスポンスヘッダーを反映した後、順番待ちの中で呼ぶ。古いセッションでは呼ばない。例外は `CLIENT_ERROR` にして共通エラーハンドラーに渡し、送り直さない
*   ログの文言から「どの反映処理か」は無くなる。スタックトレースで分かる
*   player_diff → バッジ → 通知の順は、仕様の「項目ごとの決めごと」(通信の取り決めではなく、アプリが守る決めごと)に移す

### 4. サーバーの書き方と `Common` の検査

*   アプリのサービスは今のまま。`ServiceBase<T>` を継承し、コンストラクターで `ITransactions` を受け取り、ハンドラーの中で `RunAsync` を呼ぶ。メソッド単位のフィルター属性もそのまま使える
*   `Common` を詰める処理は `RunAsync` の中に残す。理由は「共通レスポンス項目(今の作り)」のとおり
*   **検査**: 新しいグローバルフィルター `CommonResponseCheckFilter` を、基盤のフィルターの一番内側に置く。`RunAsync` は返すレスポンスを `ServiceContext.Items` に覚える。フィルターは本体の後に `GetRawResponse()` を読み、`IApiResponse<>` を実装しているのに、覚えた値と同じオブジェクトでなければ `InvalidOperationException` を投げる。`RpcLogFilter` が `INTERNAL` にする。型の判定は `CommonResponseRegistration.ImplementsApiResponse` のキャッシュを使う
*   これで、`RunAsync` の外で詰め替えた形と、`RunAsync` を通さない形の両方を検出する
*   検査は本番でも動く。検出した時点で処理はコミット済みで、クライアントには `INTERNAL` が返る。主な目的は開発中に見つけることで、動かしていない RPC では気づけない
*   `RunAsync` は 1 回の RPC で 1 回だけ呼ぶ(決めごと。利用ガイドに書く)。覚えるのは最後に返した値なので、2 回呼んで最初の値を返すと、検査が `INTERNAL` にする
*   `ThrowIfUnfilled` は残す。`AddCommonResponse` の登録忘れと `TCommon` の食い違いを検出する
*   基盤の `DeviceService` は `IApiResponse<>` を返さないので、検査に当たらない

### 5. 保存済みのレスポンス

コードは変えない。読めないときは `Deserialize` の例外が `RpcLogFilter` で `INTERNAL` になる。仕様に次を書く: 再送は失敗する。処理はコミット済みなので再実行しない。Retry では直らず、タイトルに戻るまで同じ失敗が続く。タイトルに戻れば、サインインで取り直せる。その応答の通知は失われる。型のキーの意味を変えたデプロイの前後では起こりうる。値がずれて読めた場合は検出しない。

### 6. AOT(IL2CPP)で動く前提

*   `TCommon` は `class` に限る(参照型なのでコードを共有できる)
*   `response is IApiResponse<TCommon>` は、インターフェースの型の判定で、実行時のコード生成を使わない
*   フォーマッターは、具体的なレスポンスの型ごとに MessagePack のジェネレーターが作る。`IApiResponse<>` そのもののフォーマッターは要らないはず(推測。確かめない)
*   `RpcSettings.CreateAll` は、今と同じく実行時のリフレクションで属性を読む
*   `ApiCallInvoker` の `AsyncUnaryCall<TRequest, TResponse>` は、MagicOnion の生成物から呼ばれる。`CallInvoker` を包むことで、ジェネリックの仮想メソッドの呼び出しが 1 段増える(Windows の IL2CPP で確かめない。Q8)

この節の前提は、IL2CPP で確かめずに実装する(Q8)。

### 変えないもの

ヘッダーの名前と値、ErrorInfo、予約エラー、冪等性の意味、`Common` が null なら変化なしという意味、再送で同じ `Common` を返すこと、`ICommonResponseCollector<TCommon>`・`AddCommonResponse`・`TxContext.GetCollector`、順番待ちと再認証の動き。

## Alternatives Considered

### 共通レスポンス項目の詰め方

#### Alternative A: サービスフィルターで `Common` を詰める

*   **Pros**: MagicOnion の流儀に合う。`RunAsync` を通らない RPC でも詰められる
*   **Cons**: フィルターは本体の外で動くので、保存とコミットの後になる
*   **Reason for Rejection**: 保存するレスポンスに `Common` が入らず、再送で同じ `Common` を返せない。冪等性の取り決めが崩れる

### `Common` が黙って空になる問題への対処(Q1)

#### Alternative C: 全 RPC で `RunAsync` を実行時に必須にする

*   **Pros**: トランザクションの外の書き込みも見つかる
*   **Cons**: DB を使わない RPC を外す属性が要る。公開 API が増える
*   **Reason for Rejection**: 目的は `Common` の抜けなので、`IApiResponse<>` だけを見れば足りる

#### Alternative D: Roslyn アナライザーで検出する

*   **Pros**: 実行しなくても見つかる。実行時のコストが無い
*   **Cons**: `RunAsync` の外で詰め替える形は、`RunAsync` を呼んでいるので見逃す。別メソッドに分けた呼び出しも追えない
*   **Reason for Rejection**: 主な失敗の形を検出できない

#### Alternative E: 検査しない

*   **Pros**: 部品が増えない
*   **Cons**: 破ると、エラーなしにクライアントの表示がずれる
*   **Reason for Rejection**: 通知は取り直せず、原因にも気づきにくい

#### Alternative F: 基底クラスとラッパーをコード生成する

*   **Pros**: `RunAsync` の呼び忘れを書けなくできる。属性を生成時に読める
*   **Cons**: 公式の書き方から外れる。`DrawCoreAsync` のような独自の名前が要る。MagicOnion は `protected` も含めて同じ名前のメソッドを許さない。`override` に付けたフィルター属性は効かない(2026-11-07、`FilterTests` の形で確かめた)。ジェネレーターの DLL の配布と検査も要る
*   **Reason for Rejection**: 前の版で承認したが、オーナーがアプリの書き方を公式に合わせることを選び、取り下げた

### クライアントの呼び方(Q4)

#### Alternative K: ラッパーと `WithOptions` のまま残す

*   **Pros**: 変更が無い。`handledCodes` を引数で渡せる
*   **Cons**: ラッパーを共有インターフェースごとに手で書き続ける。公式の書き方から外れたまま
*   **Reason for Rejection**: オーナーはアプリの書き方を公式に合わせることを選んだ

#### Alternative R: フィルターが `Metadata` にヘッダーを書く

*   **Pros**: 公式の書き方そのまま。`AsyncLocal` が要らない
*   **Cons**: `Metadata` はクライアントごとに使い回されるので、同時の呼び出しでヘッダーが混ざる
*   **Reason for Rejection**: `[NoSideEffects]`・`[SkipAuth]` の RPC は順番待ちをしないので、実際に同時に走る(2026-11-07 に確かめた)

#### Alternative L: アプリが呼ぶたびに `WithOptions` で締め切りとキャンセルを渡す

*   **Pros**: `CallInvoker` を包まなくてよい
*   **Cons**: 渡し忘れると、タイトルに戻っても通信が止まらない。試行ごとの締め切りも作れない
*   **Reason for Rejection**: 基盤が守る動きをアプリに任せることになる

### 失敗の伝え方(Q9)

#### Alternative O: 例外のまま呼び出し側に返す

*   **Pros**: 公式の書き方そのまま。`ToResult` が要らない
*   **Cons**: アプリが `try/catch` を書く
*   **Reason for Rejection**: オーナーが `try/catch` を非推奨にすると決めた

#### Alternative P: 業務エラーは共通エラーハンドラーに回さない

*   **Pros**: `WithHandledCodes` が要らない
*   **Cons**: 各 API でダイアログを書くことになる
*   **Reason for Rejection**: オーナーは各 API でダイアログを書かないことを選んだ

#### Alternative S: `ToResult(handledCodes)` で指定する

*   **Pros**: 引数が 1 か所にまとまる
*   **Cons**: フィルターは RPC を呼んだ時点で動くので、指定が間に合わない。共通エラーハンドラーをフィルターの外に置くと、Retry で送り直せない
*   **Reason for Rejection**: Retry はもう一度送る必要があるので、ハンドラーはフィルターの中に置く

### 保存済みのレスポンス(Q2)

#### Alternative I: 保存の行に版を持たせる

*   **Pros**: 値がずれて読める場合も防げる
*   **Cons**: 列・マイグレーション・設定が増える。上げ忘れると意味が無い。合わないときの結果は同じ失敗
*   **Reason for Rejection**: 保存するのはプレイヤーごとに最新の 1 件だけで、起きるのはデプロイをまたいだ再送だけ。失敗の結果も変わらない

### クライアントの反映処理(Q3)

#### Alternative J: VolTech-gRPC 版と同じ 3 つのまま残す

*   **Pros**: 順序を基盤が保証する。名前がそろう。実装済みのコードとテストをそのまま使える
*   **Cons**: 基盤が `TCommon` の中身の区別を持つ。`TCommon` の食い違いの検査が残る
*   **Reason for Rejection**: 基盤は中身を知らない前提なので、順序はアプリの決めごとにする。RFC の規則でも、gRPC 版にそろえるのはベストプラクティスとぶつからない範囲だけ

### アプリのフィルター(Q5)

#### Alternative N: アプリがフィルターを基盤より内側に足せる

*   **Pros**: アプリ固有の処理を基盤を直さずに足せる
*   **Cons**: 公開 API が増える。アプリのフィルターが再試行や打ち切りをすると、順番待ち・再認証・冪等性の前提を崩すおそれがある
*   **Reason for Rejection**: 今の使い道が無い。必要になったら足す

--------------------------------------------------------------------------------

## Non-Functional Requirements

### Security & Privacy

*   **トークン**: `ApiCallContext` に入り、`ApiCallInvoker` が `Metadata` に入れる。プロセスの中のメモリーにあり、今と扱いは変わらない。`IApiLogger` には出さない。Monitor(設計書 4)が `CallInvoker` で記録するなら、`authorization` を読めるので、表示で隠すことを設計書 4 で決める
*   **内部のヘッダー**: `core.CreateClient<T>()` で作ったクライアントでは、`x-voltech-call` は `ApiCallInvoker` が写さないので送られない。テストで確かめる。`deviceClient` に付けるとサーバーに届くが、中身はエラーコードの名前だけ

### Rollout & Rollback

*   クライアントとの取り決めも DB のスキーマも変えないので、サーバーとクライアントはどちらを先に更新しても通信できる
*   基盤の公開 API は壊れる。Uta は基盤の版を上げるときに一緒に移す。戻すときは基盤の版を戻す

### Risks

*   **E2E は CI で動かない**: Unity と Docker が要るので、手で動かす。動かし忘れると、通信の退行に気づけない
*   **E2E は Editor の gRPC で動く**: Editor では Burst が同梱する gRPC 2.61.0 が読み込まれる(`docs/index.md`)。本番の 2.84.0 での確認にはならない
*   **IL2CPP で確かめない**: Q8 の決定で、Technical Plan の「6. AOT(IL2CPP)で動く前提」を IL2CPP で確かめない。`CallInvoker` の包み方、`TCommon` の判定、フォーマッターが IL2CPP だけで違っても、この設計書の作業の中では見つからない
*   **`RunAsync` を通らないレスポンスは書ける**: 検査は実行時で、動かした RPC でしか気づけない。検出はコミットの後になる。Q1 の決定で受け入れる
*   **`ToResult` の付け忘れ**: コンパイルでは分からない。ダイアログは出た後、呼び出し側に `ApiException` が届く。利用ガイドに書く
*   **`IService<T>` の API が基盤と重なる**: アプリが `WithHandledCodes` の後に `WithHeaders`・`WithOptions` を呼ぶと、指定が消えて共通エラーハンドラーに回る。アプリが付けたヘッダー、`WithDeadline`、`WithCancellationToken` は `ApiCallInvoker` が置き換えるので効かない。利用ガイドに書く
*   **`ApiCore` の送信の処理を書き換える**: 順と分岐は保つが、呼び出しの形が変わる。テストを先に本物のクライアントを通す形に移し、今のコードで通してから本体を変える
*   **`next` の中で `CallInvoker` まで進むこと**: 進まないと、`ApiCallInvoker` が `ApiCallContext` を読めない。読めなければ例外になるので、黙ってヘッダーなしで送ることはない

--------------------------------------------------------------------------------

## Detailed Implementation Plan

### 決めたこと

*   **受け継いだメソッド**: Technical Plan の「2. クライアントの処理の移し先」のとおり
*   **`ApiResult<T>`**: `IsSuccess` を `IsError` に変え、`ErrorCode`(`ErrorCode?`。失敗のときだけ値がある)と `ErrorMessage`(失敗のときだけ値がある)を足す
*   **`x-voltech-call` の値**: `activity=<0|1>;codes=<domain>/<reason>,<domain>/<reason>` の形。`activity` と `codes` はいつも書く(`codes` は空でよい)。`domain` と `reason` は `Uri.EscapeDataString` で書く。読めない値は `InvalidOperationException`(基盤の不具合)。`ApiHeaders` には入れない。送らないので、クライアントとの取り決めではない
*   **`DeviceTokenProvider`**: 変えない。テスト(`DeviceTokenProviderTests`)も変えない
*   **ログの文言**: 「どの反映処理か」が消えることは Q3 の結果として受け入れる。`IApiLogger` の仕組みは変えない

### 実装の順番

1.  **設計書を承認する**: オーナーが承認してから実装に入る。Status を Approved にし、一覧に載せる(ファイル 1・2)
2.  **未確認の項目を確かめる**(下の「実装の前に確かめた項目」)。済んだ
3.  **ドキュメント**: `docs:` のコミットを先に置く(ファイル 1 の確認の結果、ファイル 3〜11)
4.  **サーバー**: `Common` の検査(ファイル 12〜16)
5.  **反映処理**: 反映処理を 1 つにまとめる(ファイル 25 の「段階 5」、26 の「段階 5」、29、30、33 の「段階 5」)。この段階ではラッパーのまま
6.  **テストの移行**: 本体を変えず、テストだけを本物のクライアントと `FakeCallInvoker` に移し、今のコードで全部通す(ファイル 32・33 の「段階 6」、34、35)
7.  **クライアント**: `ApiCore` をフィルターと `CallInvoker` から動かす形にし、アプリの呼び方を変える(ファイル 17〜24、25・26 の「段階 7」、27、28、31、32・33 の「段階 7」)
8.  **実サーバーでの確認**: テスト用のサーバーと PlayMode のテストを作り、Unity から本物のサーバーを呼ぶ(ファイル 36〜38)。設計書の承認の後に、オーナーの指示で足した(2026-11-07)。これが通るまで、`main` に入れない
9.  **全体の検証**: Verification Plan を実行し、結果と実装で分かった API 名の違いを設計書と `docs/index.md` に戻す(ファイル 1・10)

実装は 1 本のブランチで行い、段階ごとにテストが通る形でコミットする。Uta の変更はこの設計書に含まない。基盤の版を上げるときに、Uta のリポジトリで 7 RPC の呼び方と組み立ての処理を移す。サーバーのサービスは書き方が変わらない。

#### 実装の前に確かめた項目

2026-11-07 に、EditMode のテスト `ClientBehaviorTests`(ソースジェネレーターのクライアントと偽の `CallInvoker`)で確かめた。

*   **A1**: 本物の `MagicOnionClient` と偽の `CallInvoker` を EditMode で動かせる
*   **A2**: 「クライアントの呼び方」の節の確認結果は、ソースジェネレーターのクライアントでも同じ。`TResponse`・`next`・`Metadata`・`WithHeaders`・例外の型を確かめた
*   **生成の範囲**: `[MagicOnionClientGeneration]` に書いた型の**アセンブリ全体**の共有インターフェースが生成される。名前を書いていなくても、同じアセンブリなら作られる
*   **生成の対象でないアセンブリ**: `MagicOnionClient.Create<T>` が `NotSupportedException` で失敗する。コンパイルでは分からない
*   **受け継いだメソッド**: ソースジェネレーターは基底インターフェースのメソッドの実装を作らず、アプリのコンパイルエラー(CS0535)になる。実行時に届く経路が無いので、`InvalidOperationException` の分岐は作らない

### Target Files to Create/Modify

`Unity/` の下に足すファイルの `.meta` は Unity が作るので、ファイルと一緒にコミットする。消すファイルの `.meta` も一緒に消す。下の一覧には個別に挙げない。

#### ドキュメント

#### 1. `docs/designs/common-response-and-client-filter.md` (Modify)

*   **Type / Scope**: 設計書
*   **Proposed Changes**: 段階 1 で Status を Approved にする。段階 2 の結果を段階 3 で、実装で分かった API 名の違いを段階 9 で戻す

#### 2. `docs/designs/index.md` (Modify)

*   **Type / Scope**: 設計書の一覧
*   **Proposed Changes**: 段階 1 で、この設計書を「検討中」から「承認済み」に移す。CLAUDE.md の手順 4

#### 3. `docs/spec/common-response.md` (Modify)

*   **Type / Scope**: 仕様書
*   **Proposed Changes**: 反映処理が 1 つになるので、「クライアント」の節を直す。基盤が順序を保証する記述をやめ、「反映処理へ `Common` を渡す。省略できる」にする。「残りは呼ばない」を消す。`TCommon` の食い違いの項目を消す。順序はアプリの決めごととして、「項目ごとの決めごと」に足す(player_diff → バッジ → 通知)。「サーバー」の節に、`RunAsync` を通らずに返した `IApiResponse<>` は `INTERNAL` になることを足す

#### 4. `docs/spec/headers.md`・`docs/spec/errors.md`・`docs/spec/processing-order.md` (Modify)

*   **Type / Scope**: 仕様書
*   **Proposed Changes**: ヘッダーの表は変えない。ラッパー(`ApiServiceBase<TService>`)の説明を、クライアントが共有インターフェースを直接呼ぶ形に直す。受け継いだメソッドの `ArgumentException` を、アプリのコンパイルエラーに直す。`errors.md` の `handledCodes` を `WithHandledCodes` に、`../guide/client.md#ラッパー` へのリンクを新しい節に直す。`processing-order.md` の処理の段に `CommonResponseCheckFilter` を足す

#### 5. `docs/spec/transaction.md` (Modify)

*   **Type / Scope**: 仕様書
*   **Proposed Changes**: 「冪等性」の節に Q2 の決定を書く: 保存済みのレスポンスを読めなければ `INTERNAL`。処理はコミット済みなので再実行しない。Retry では直らず、タイトルに戻るまで同じ失敗が続く。タイトルに戻ればサインインで取り直せる。その応答の通知は失われる。値がずれて読めた場合は検出しない

#### 6. `docs/designs/implementation/client.md` (Modify)

*   **Type / Scope**: 実装設計書
*   **Proposed Changes**: 冒頭の「`ApiCoreBuilder` の公開 API は残し、アプリ側のコードは書き直さずに済む」の記述を直す。冒頭の表の `WithOptions` でヘッダーを渡す記述、反映処理 3 つの行、`ApiServiceBase` の行を直す。「移したもの」の表の `ApiCore`・`ApiServiceBase`・`RpcSettings` の行を直す(`DeviceTokenProvider` の行は変えない)。「ラッパーの書き方」と「共通レスポンス項目の反映」を、この設計書へのリンクに置き換える。「移していないもの」の `FakeCallInvoker` の説明を、ファイル 34 の方式に合わせて直す。「テスト」の節を、ファイル 32〜34 の内容に合わせる

#### 7. `docs/designs/implementation/server.md`・`docs/designs/implementation/project-structure.md` (Modify)

*   **Type / Scope**: 実装設計書
*   **Proposed Changes**: `project-structure.md` の `Transaction/` の行に `CommonResponseCheckFilter` を足す。`server.md` のフィルターの表と、基盤のフィルターの段の数の記述に `CommonResponseCheckFilter` を足す。「共通レスポンス項目」の節に、この設計書へのリンクを足す。コレクターの表と説明は、この設計書が変えないので残す

#### 8. `docs/guide/client.md`・`docs/guide/index.md` (Modify)

*   **Type / Scope**: 利用ガイド
*   **Proposed Changes**: 「組み立て」を `ApiCore.CreateBuilder` の新しい引数と `core.CreateClient<T>()` の形に直す。「`ApiCore` はアプリで 1 つにする」は残す。「共通レスポンス項目の反映」を `WithCommonResponseApplier` に直し、順序はアプリが守ると書く。「ラッパー」の節を「呼び方」に変え、`ToResult`・`WithHandledCodes`・`WithoutActivity(codes)`、`WithHandledCodes` と `WithoutActivity` を続けてつながないこと、`ToResult` の付け忘れ、`WithHeaders`・`WithOptions`・`WithDeadline`・`WithCancellationToken` を使わないこと、失敗の偽物は `FakeRpcErrors.ApiFailureResult<T>` を返すことを書く。`try/catch` を使わないと書く。「VolTech-gRPC 版から移すとき」の、ラッパーと 3 つの反映処理の記述を直す。`index.md` の「ラッパーの書き方」を「呼び方」に直す

#### 9. `docs/guide/server.md` (Modify)

*   **Type / Scope**: 利用ガイド
*   **Proposed Changes**: ハンドラーの書き方は変えない。`IApiResponse<>` のレスポンスを `RunAsync` の外で作ると `INTERNAL` になることと、`RunAsync` は 1 回の RPC で 1 回だけ呼ぶことを、「共通レスポンス項目」の節に足す。クライアントのラッパー(`ApiServiceBase<TService>`)に触れた記述を直す

#### 10. `docs/index.md` (Modify)

*   **Type / Scope**: 計画とチェックリスト
*   **Proposed Changes**: Unity クライアントの行の `WithPlayerDiffApplier` などを直す。「確かめること」に「実装の前に確かめた項目」を、日付と方法を添えて足す。IL2CPP と iOS の既存の項目は、確かめないまま残す(Q8)。`ApiServiceBase` が属性をリフレクションで読めるかの項目は、`RpcSettings` が読む形に書き換える。「決めること」の反映処理の記述(「3 つ」)を直す。`implementation/client.md#ラッパーの書き方` へのリンクをこの設計書へのリンクに直す。「進め方」は経過の記録なので直さない

#### 11. `CHANGELOG.md` (Modify)

*   **Type / Scope**: 変更の記録
*   **Proposed Changes**: 「互換性が壊れる変更」に、`ApiServiceBase` の削除、呼び方の変更、`ApiCore.CreateBuilder` の引数、反映処理、`ApiResult<T>` を書く。「Added」に `ApiCore.CreateClient<T>`・`ToResult`・`WithHandledCodes`・`WithoutActivity`・`ApiException`・`FakeRpcErrors.ApiFailureResult<T>`・`CommonResponseCheckFilter`・`FilterOrder.CommonResponseCheck` を書く

#### サーバー

#### 12. `src/VolTech.Server/Transaction/CommonResponseCheckFilter.cs` (Create)、`src/VolTech.Server/Transaction/CommonResponseRegistration.cs` (Modify)

*   **Type / Scope**: 内部のフィルター
*   **Proposed Changes**: Technical Plan の 4 節の検査をする `IMagicOnionServiceFilter`。`CommonResponseRegistration.ImplementsApiResponse` を internal にして使う

#### 13. `src/VolTech.Server/Transaction/Transactions.cs` (Modify)

*   **Type / Scope**: トランザクション
*   **Proposed Changes**: `RunAsync` が返すレスポンスを、すべての成功の経路(保存済みのレスポンスを返す経路を含む)で `ServiceContext.Items` に覚える

#### 14. `src/VolTech.Server/Internal/ContextItems.cs` (Modify)

*   **Type / Scope**: 内部の定数
*   **Proposed Changes**: 覚えるレスポンスのキーを足す

#### 15. `src/VolTech.Server/Hosting/VoltechServerExtensions.cs` (Modify)

*   **Type / Scope**: 登録
*   **Proposed Changes**: `CommonResponseCheckFilter` を `GlobalFilters` に足す。`FilterOrder` に公開の定数 `CommonResponseCheck` を足し、`DateChange` より内側にする

#### 16. `tests/VolTech.Server.Tests/CommonResponseTests.cs`・`Support/CommonResponseTestServices.cs` (Modify)

*   **Type / Scope**: 結合テスト
*   **Proposed Changes**: 次を足す: `RunAsync` の外で詰め替えると `INTERNAL`、`RunAsync` を通さないと `INTERNAL`、同じ `x-request-id` の再送は検査を通る、保存済みのレスポンスを読めないと `INTERNAL` になり本体を呼ばない(CUJ 5)

#### クライアント

#### 17. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Internal/ApiCallFilter.cs` (Create)

*   **Type / Scope**: 内部のフィルター
*   **Proposed Changes**: `IClientFilter` を実装する薄いクラス。コンストラクターは `ApiCore` と `RpcSettings` の表を受け取る。`MethodPath` で設定を引き、`x-voltech-call` を読み(`Metadata` は書き換えない)、`ApiCore.SendAsync` を呼ぶだけにする

#### 18. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Internal/ApiCallInvoker.cs` (Create)

*   **Type / Scope**: 内部の型
*   **Proposed Changes**: `CallInvoker` を包む。`AsyncLocal<ApiCallContext>` をインスタンスで 1 つ持つ(static にしない)。`AsyncUnaryCall` で値を読み、Technical Plan の「2. クライアントの処理の移し先」のヘッダーだけを入れた新しい `Metadata` を作る。`options.WithHeaders(新しい Metadata).WithDeadline(今 + ApiCoreOptions.Deadline).WithCancellationToken(ApiCallContext.CancellationToken)` で内側を呼び、`ReadResponse` を入れる。`ApiCallContext` が null なら `InvalidOperationException`。ほかの呼び方は `NotSupportedException`(基盤は Unary だけ)

#### 19. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Internal/ApiCallContext.cs` (Create)

*   **Type / Scope**: 内部の型
*   **Proposed Changes**: `Token`・`RequestId`・`GameDate`・`CancellationToken` と、`ApiCallInvoker` が入れる `ReadResponse`(`Func<object>`)を持つ。試行ごとに作る

#### 20. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Errors/ApiException.cs` (Create)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `ApiError` を持つ例外。`ToResult` を付けなかったアプリに届く型。コンストラクターは internal

#### 21. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiClientExtensions.cs` (Create)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `T : IService<T>` の拡張メソッド `WithHandledCodes(params ErrorCode[] codes)` と `WithoutActivity(params ErrorCode[] codes)`。`x-voltech-call` を `WithHeaders` で渡す。ヘッダーを読む internal の `ReadCallSettings` もここに置く

#### 22. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiResultExtensions.cs` (Create)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `UnaryResult<T>` の拡張メソッド `UniTask<ApiResult<T>> ToResult()`。`ApiException` を受け止めて失敗の結果にする。それ以外の例外はそのまま投げる(不具合を隠さない)

#### 23. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiResult.cs` (Modify)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: 「決めたこと」のとおり。`Value` の動きは変えない

#### 24. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ErrorHandling/ICommonErrorHandler.cs` (Modify)

*   **Type / Scope**: XML コメント
*   **Proposed Changes**: 「handledCodes で受け取らなかったエラー」を「`WithHandledCodes` で指定しなかったエラー」に直す。動きは変えない。`RpcSettings` は変えない

#### 25. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiCore.cs` (Modify)

*   **Type / Scope**: 共通処理
*   **Proposed Changes**: 段階 5: `commonAppliers` を `Func<object, UniTask>` 1 つ(`ApiCoreBuilder.ApplyCommonResponse`)に変え、`ApplyCommonFieldsAsync` のループと `applying` を消す。段階 7: `CreateBuilder` の引数を `(CallInvoker callInvoker, IMagicOnionSerializerProvider serializerProvider, IMagicOnionClientFactoryProvider clientFactoryProvider, ApiCoreOptions options, ITokenProvider tokenProvider, IErrorPolicyProvider errorPolicy, ICommonErrorHandler errorHandler)` に変える。`ApiCallInvoker` を 1 つ持つ。`CreateClient<T>()` を足す。`RpcSettings.CreateAll(typeof(T))` で `ApiCallFilter` を作り、`MagicOnionClient.Create<T>(ApiCallInvoker, serializerProvider, フィルター, clientFactoryProvider)` を呼ぶ(7.11.0 にこの形があることをリフレクションで確かめた。2026-11-07)。`InvokeAsync` を internal の `SendAsync(RpcSettings, RequestContext, Func<RequestContext, ValueTask<ResponseContext>>, IEnumerable<ErrorCode>, bool)` に変え、処理は private のまま残す。`SendOnceAsync` は `ApiCallContext` を作り、`ApiCallInvoker.Send` で送る。`Send` は `AsyncLocal` に値を入れて `next(context)` を呼び、`ValueTask` を受け取った直後(`await` の前)に null に戻す。`CreateHeaders` を消し、ヘッダーの組み立ては `ApiCallInvoker` に移す。`ApplyCommonFieldsAsync` は `ReadResponse` で読む。失敗は `ApiException` を投げ、古いセッションは `ApiError.Discarded()` の `ApiException` にする

#### 26. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiCoreBuilder.cs` (Modify)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: 段階 5: `WithPlayerDiffApplier`・`WithBadgeApplier`・`WithNotificationApplier` と 3 つの internal プロパティを消し、`WithCommonResponseApplier<TCommon>(ICommonResponseApplier<TCommon> applier)` を足す。中のクロージャーが `response is IApiResponse<TCommon>` で判定し、`Common` が null でなければ `ApplyAsync` を呼ぶ。これを internal プロパティ `ApplyCommonResponse` に入れる。null を渡すと渡さなかったのと同じで、設定済みなら外れる。2 回呼ぶと後のほうで置き換わる。`Build` の検査と `CommonAppliers()` を消す。段階 7: コンストラクターに `CreateBuilder` の増えた 3 つの引数を足す。どれか 1 つでも null なら `ArgumentNullException`

#### 27. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiServiceBase.cs` (Delete)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: アプリが共有インターフェースを直接呼ぶので不要になる

#### 28. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/ApiChannel.cs` (Modify)

*   **Type / Scope**: XML コメント
*   **Proposed Changes**: `CallInvoker` の渡し先のコメントに `ApiCore.CreateBuilder` を足す

#### 29. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Hooks/ICommonResponseApplier.cs` (Create)

*   **Type / Scope**: 公開 API
*   **Proposed Changes**: `UniTask ApplyAsync(TCommon common)` を持つ。`where TCommon : class`。XML コメントに、player_diff → バッジ → 通知の順で反映すること、データの更新だけをすることを書く

#### 30. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Hooks/IPlayerDiffApplier.cs`・`IBadgeApplier.cs`・`INotificationApplier.cs`、`Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Internal/CommonApplier.cs` (Delete)

*   **Type / Scope**: 公開 API と内部の型
*   **Proposed Changes**: Q3 の決定で不要になる。Uta はリリース前なので、残す期間は設けない

#### 31. `Unity/com.chinpangx.voltech.magiconion/Runtime/Client/Testing/FakeRpcErrors.cs` (Modify)

*   **Type / Scope**: アプリのテスト用の公開 API
*   **Proposed Changes**: `public static UnaryResult<T> ApiFailureResult<T>(ErrorCode code, string message = "", ErrorAction action = ErrorAction.Unspecified)` を足す。`ApiException` で失敗した `UnaryResult<T>` を返す。`ApiError` の `StatusCode` は `FailedPrecondition`。アプリの偽の `IGachaService` が返すと、`ToResult` が失敗の結果にする。`ApiError` のコンストラクターは internal のまま

#### 32. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/ApiCoreTests.cs` (Modify)

*   **Type / Scope**: EditMode テスト
*   **Proposed Changes**: 段階 6: 偽のサービスをやめ、`FakeCallInvoker` と本物のクライアントを `TestApi` に渡す形に書き直す。本体は変えない。確認の内容も変えず、今のコードで全部通すことを確かめる。ヘッダーの確認は `FakeCallInvoker` に記録したヘッダーを読む形にする。`FakeTokenProvider`・`RecordingErrorHandler` はこのファイルに残す。`ITestService` に値型を返す RPC を足す。別のサービスの確認に使う共有インターフェースを足す(同じアセンブリなので生成の対象になる)。段階 7: `core.CreateClient<ITestService>()` と `ToResult()` に変える。`handledCodes` は `WithHandledCodes` にする。`TestApi` と未知のメソッドのテストを消す。`RpcSettings.CreateAll` のテストは残す。段階 7 で次を足す: 別のサービスのクライアントどうしでも冪等性の対象の RPC が順番に送られること、`x-voltech-call` がサーバーに送られないこと、同じクライアントの同時の呼び出しでヘッダーが混ざらないこと、`WithHandledCodes` のクライアントを変数に持って 2 回呼んでも指定が効くこと、試行ごとに締め切りが新しくなること、順番待ちや認証トークンの取得の間に `ResetSession` すると、`FakeCallInvoker` の `Func` が呼ばれず、結果の `Error.IsDiscarded` が true になること、`ToResult` を付けないと `ApiException` が届くこと、`WithoutActivity` で進行中を通知しないこと、`ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED` が共通エラーハンドラーを通らず `IsError` の結果になること、`FakeRpcErrors.ApiFailureResult<T>` を `ToResult` が失敗の結果にすること、値型のレスポンスの RPC が反映処理があっても動くこと、アプリの `WithHeaders` のヘッダーが送られないこと

#### 33. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/CommonResponseTests.cs` (Modify)

*   **Type / Scope**: EditMode テスト
*   **Proposed Changes**: 段階 5: `AppliesInOrder` は、反映処理 1 つが `Common` を受け取り、呼び出し側に返る前に反映が終わることの確認に書き直す。3 つのうち一部を渡すテスト(`OnlyGivenAppliersAreCalled`)と `TCommon` の食い違いのテストを消す。次を足す: 反映処理を渡さなければ反映しないこと、`WithCommonResponseApplier` に null を渡すと反映しないこと(設定済みなら外れる)、2 回呼ぶと後のものだけが動くこと、レスポンスが `IApiResponse<TCommon>` でない・違う `TCommon` のときは反映しないこと。`Common` が null なら呼ばないこと、例外で `CLIENT_ERROR` になり送り直さないこと、古いセッションでは反映しないことは残す。「`IApiResponse<TCommon>` でない」の確認に使うレスポンスの型と `ICommonTestService` のメソッド、`FakeCommonService` の対応もこの段階で足す。「違う `TCommon`」は既存の `OtherFields` の反映処理を渡して確かめる。段階 6: `FakeCallInvoker` と本物のクライアントに移す。`CommonResult`・`CommonFields` と段階 5 で足した型に `[MessagePackObject]` と `[Key]` を付ける。段階 7: `core.CreateClient<ICommonTestService>()` と `ToResult()` に変え、`CommonApi` を消す

#### 34. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/Support/FakeCallInvoker.cs`・`Support/TestClients.cs`・`ClientBehaviorTests.cs` (Create)

*   **Type / Scope**: EditMode テストの部品
*   **Proposed Changes**: `FakeCallInvoker` はテストの中だけで使う(`Runtime/Client/Testing/` には置かない)。`AsyncUnaryCall<TRequest, TResponse>` だけを実装し、ほかは `NotSupportedException` にする。`method.FullName`、呼び出しの順番、`options` を記録し、テストが設定した `Func<FakeCall, Task<FakeResult>>` を呼ぶ。`options.CancellationToken` がキャンセル済みなら、`Func` を呼ばず `RpcException(StatusCode.Cancelled)` にする(本物の gRPC と同じ動き。記録は残す)。`Task` を返すので、`TaskCompletionSource` で応答を保留でき、呼び出しの順番やメソッド名で結果を変えられ、デリゲートの中で `ResetSession` を呼べる。`FakeResult` の成功は、レスポンスを MessagePack にしたバイト列とレスポンスヘッダー。バイト列は `method.ResponseMarshaller.ContextualDeserializer` と、テストの `DeserializationContext` で読んで返す(スクラッチのプログラムと同じ)。失敗は `RpcException`。バイト列にするのは、MagicOnion が内部の型で通信するので、型付きの値を渡せないため。`options` の `Metadata` は使い回されるので、呼ばれた時点の中身を写して記録する。`TestClients` は `partial` のクラスで、`[MagicOnionClientGeneration]` にテストのアセンブリの型を書く。`SerializerProvider` は、生成された `Resolver` と `StandardResolver.Instance` の `CompositeResolver` から作り(`Unity/VolTech.Sandbox/Assets/Smoke/SmokeClient.cs` の `RunAsync` と同じ)、`ClientFactoryProvider` は生成されたものを返す。`ClientBehaviorTests` は MagicOnion の動きの確認のテストで、そのまま残す

#### 35. `Unity/com.chinpangx.voltech.magiconion/Tests/EditMode/VolTech.MagicOnion.Client.Tests.EditMode.asmdef` (Modify)

*   **Type / Scope**: ビルドの設定
*   **Proposed Changes**: 本物のクライアントを動かすのに要る参照を足す。この asmdef は `overrideReferences: true` なので、`precompiledReferences` に `MagicOnion.Client.dll`・`MagicOnion.Shared.dll`・`MagicOnion.Serialization.MessagePack.dll`・`MessagePack.dll` のようにファイル名で書く。`ClientBehaviorTests` で確かめた一覧

#### 実サーバーでの確認

2026-11-07 に PlayMode の 12 件が通った(`tests/VolTech.E2E.Server` を相手に、YetAnotherHttpHandler で通信した)。

#### 36. `tests/VolTech.E2E.Server/VolTech.E2E.Server.csproj`・`Program.cs`・`E2EService.cs` (Create)、`VolTech-MagicOnion.slnx` (Modify)

*   **Type / Scope**: テスト用のサーバー
*   **Proposed Changes**: 本物の `AddVoltechServer` を Kestrel の h2c(既定のポートは 5199)で起動する実行ファイル。PostgreSQL は Testcontainers で起動してマイグレーションを流す。`IPlayerResolver` はいつも `Active` を返す。共有インターフェースは `Unity/VolTech.Sandbox/Assets/E2E/Shared/` のソースを `Compile Include` で読み込む。`E2EService` は確認用の RPC を持つ

#### 37. `Unity/VolTech.Sandbox/Assets/E2E/Shared/IE2EService.cs`・`VolTech.Sandbox.E2E.Tests.asmdef`・`E2EClients.cs`・`E2ETests.cs` (Create)

*   **Type / Scope**: PlayMode テスト
*   **Proposed Changes**: Sandbox の PlayMode テスト。`ApiChannel`(YetAnotherHttpHandler)、本物の `DeviceTokenProvider`、`ApiCore.CreateClient<T>` で、ファイル 36 のサーバーを呼ぶ。サーバーが動いていなければ失敗する(スキップしない)。接続先は環境変数 `VOLTECH_E2E_ADDRESS` で変えられる。確かめること: 端末登録とトークンの発行、`authorization`・`x-request-id`・`x-app-version`・`x-game-date` がサーバーに届くこと、`[SkipAuth]` の RPC、`Common` の反映、応答を失ったときの再送で本体を呼ばないこと、トークンを消したときの再認証、業務エラーの `ErrorCode` と metadata(`grpc-status-details-bin` が届くこと)、`INTERNAL`、`RunAsync` を通らないレスポンスの検査、`ResetSession` で通信が止まること、締め切りで通信が止まること

#### 38. `README.md`・`docs/designs/implementation/project-structure.md`・`docs/index.md`・`docs/guide/client.md` (Modify)

*   **Type / Scope**: 文書
*   **Proposed Changes**: `README.md` の「開発」に、サーバーを起動して PlayMode のテストを動かす手順を書く。`project-structure.md` に `tests/VolTech.E2E.Server` と `Assets/E2E` を足す。`docs/index.md` の「確かめること」の、YetAnotherHttpHandler でトレーラーが届くかの項目を、結果に合わせて直す。`Assets/Smoke` の説明の「相手のサーバーはまだリポジトリに入れていない」は、`SmokeClient` についての記述なので残す

### Verification Plan

*   **Compilation/Build**: `dotnet build VolTech-MagicOnion.slnx --configuration Release`。Unity は Sandbox(7000.0.0a12)でコンパイルが通り、パッケージのコードに UAL0010 の警告が出ないこと
*   **Unit Tests**: `dotnet test VolTech-MagicOnion.slnx --configuration Release`(Docker が要る)。`unity test <Sandbox のパス> --mode EditMode`(Unity CLI。実装設計書のクライアントの「テスト」と同じ)
*   **Static Analysis & Linting**: `.editorconfig` のコードスタイルはビルドで確かめる。次の検索で 0 件になること(大文字小文字を区別する): `Unity/com.chinpangx.voltech.magiconion` と `docs/guide`・`docs/spec` の `ApiServiceBase`・`CreateHeaders`・`CommonApplier`・`IPlayerDiffApplier`・`IBadgeApplier`・`INotificationApplier`。`docs/guide`・`docs/spec` の `handledCodes`
*   **Test Coverage Requirement**: 数値の目標は置かない。下の表の項目を、表に書いた場所ですべて確かめる
*   **E2E/Integration**: `dotnet run --project tests/VolTech.E2E.Server` でサーバーを起動し、`unity test <Sandbox のパス> --mode PlayMode` で全部通す。Docker が要る。CI では動かさない(Unity と Docker が要るため)ので、クライアントを変えたときは手で動かす。IL2CPP のプレイヤーでの確認は、この設計書の完了条件に入れない(Q8)

| 確かめること | 確かめる場所 |
| --- | --- |
| CUJ 1: `RunAsync` を通らないレスポンスが `INTERNAL` になる | `tests/VolTech.Server.Tests/CommonResponseTests` |
| CUJ 2: ヘッダーが付き、呼び出し側に返る前に反映が終わる | EditMode の `ApiCoreTests`、`CommonResponseTests` |
| CUJ 3: 再認証と、同じ `x-request-id` での再送 | EditMode の `ApiCoreTests` |
| CUJ 4: 共通エラーハンドラーと `WithHandledCodes`、`IsError`・`ErrorCode`・`ErrorMessage` | EditMode の `ApiCoreTests` |
| CUJ 5: 保存済みのレスポンスを読めないと `INTERNAL` になり、本体を呼ばない | `tests/VolTech.Server.Tests/CommonResponseTests` |
| ヘッダーの名前・値・条件が変わっていない | EditMode の `ApiCoreTests`、`tests/VolTech.Server.Abstractions.Tests/ApiHeadersTests`(既存。変えない) |
| 締め切りとセッションのキャンセルが通信に届く | EditMode の `ApiCoreTests` |
| `ACCOUNT_DATA_CORRUPTED`・`ACCOUNT_SAVE_FAILED` が共通エラーハンドラーを通らず、呼び出し側に返る | EditMode の `ApiCoreTests` |
| 同じクライアントの同時の呼び出しでヘッダーが混ざらない | EditMode の `ApiCoreTests` |
| 別のサービスのクライアントどうしでも順番待ちが効く | EditMode の `ApiCoreTests` |
| 古いセッションの呼び出しが通信に進まない(`FakeCallInvoker` の `Func` が呼ばれない) | EditMode の `ApiCoreTests` |
| 内部のヘッダーが送られない | EditMode の `ApiCoreTests` |
| Unity のクライアントが本物のサーバーと通信できる(ファイル 37 の項目) | PlayMode の `E2ETests` |

*   **実装の後**: `/egm:egm-drift-report` で設計書とコードのずれを確かめ、ずれは設計書に戻す

--------------------------------------------------------------------------------

*Drafted with EGM*
