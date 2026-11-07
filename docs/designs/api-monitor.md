# Monitor(通信を 1 件ずつ見る Editor のウィンドウ)

*   **Author:** ちんぱん
*   **Status:** Approved
*   **Date:** 2026-11-08

--------------------------------------------------------------------------------

## Overview

[基盤の方針と進める順番](foundation-direction.md)(RFC)の「設計書 4」。Unity Editor で、API のリクエストとレスポンスを 1 件ずつ見るウィンドウ(Monitor)を作る。VolTech-gRPC にある gRPC Monitor を、MagicOnion の基盤に作り直す。この基盤を使うゲームは Uta で、リリース前。

手本は別のリポジトリにある。コミット `6dedbde` の時点の、`VolTech-gRPC/docs/wiki/client/grpc-monitor.md` と `VolTech-gRPC/Unity/com.chinpangx.voltech.grpc/` の `Runtime/Tracing/`・`Editor/`・`Tests/EditMode/ApiTraceTests.cs` を指す。以下で「gRPC 版」はこれらのこと。「設計書 1」は[共通レスポンス項目とクライアントの呼び方](common-response-and-client-filter.md)のこと。

作るものは 2 つ。

1. **通信の記録**: `ApiChannel.CreateCallInvoker` で作る `CallInvoker` に、記録の `Interceptor` を付ける。この `CallInvoker` を通る通信を、トークンの発行も含めて全部記録する。ヘッダーと締め切りは、基盤が渡した値をそのまま記録する。Body は別にシリアライズして持ち、クライアントの型で読み直して見せる
2. **ウィンドウ**: gRPC 版と同じ構成(Toolbar、一覧の 4 列、詳細の Foldout)。UI Toolkit を C# のコードだけで組み立てる

gRPC 版との大きな違いは 2 つ。

*   **Body の JSON の作り方**: protobuf は `JsonFormatter` で項目名の付いた JSON になる。MagicOnion の MessagePack は `[Key(0)]` の型を配列で書くので、そのままでは項目名が消える。開発用なので、リフレクションで `[Key]` のメンバーをたどって項目名を出す
*   **実機の表示を今回は作らない**: USB の実機(PlayerConnection)の表示は後の段階に分ける。RFC の Goal を直す

見るだけのツールで、通信の結果を変えない。記録の部品は Editor だけに入る。プレイヤーのビルドには、開発ビルドでもアセンブリごと入らない。gRPC 版は開発ビルドにも入れるが、今回は実機の表示を作らないので、入れても記録を読む人がいない。

RFC の順番では設計書 2(ログと計測)・3(ゲームログ)・5(`StoredAccount`・`RpcErrorDetails`)が先だが、どれもまだ書いていない。Monitor を先に作るのはオーナーの判断(2026-11-07 に依頼)。設計書 1 とサンプルで通信の層が固まった今なら、作り直しの手戻りが少なく、以降の設計書の実装でも通信を確かめる道具として使える。RFC の Analysis 7 は「Uta の開発で困るまで後回しにできる」と書いているが、作ること自体は 2026-11-04 に決めている。

設計書 2・3 とは部品が重ならない。設計書 5 とは `RpcErrorDetails.GetErrorInfo(RpcException)` だけが重なる。戻り値の形が変わるときは設計書 5 で一緒に直す。

## The Problem

設計書 1 の実装で、アプリは共有インターフェースを直接呼び、ヘッダーと締め切りは包んだ `CallInvoker` が付ける形になった。そのため、通信で何が起きているかがコードから見えにくい。

*   実際に送ったヘッダー(`authorization`・`x-request-id`・`x-app-*`・`x-game-date`)は `ApiCallInvoker` の中で作るので、アプリからは見えない
*   再認証や再試行で送り直すと、呼び出しは 1 回でも通信は 2 回になる。同じ `x-request-id` で並んでいるかを確かめる手段が無い
*   `ToResult()` が返すのは `ErrorCode` と `ErrorMessage` だけで、`ErrorInfo` の metadata やレスポンスヘッダーは見えない
*   トークンの発行は `IDeviceService` を直接呼ぶので、`ApiCore` を通らない。`ApiCore` の中でログを足しても見えない
*   レスポンスの `Common` が入っているかは、反映の結果からしか分からない

`IApiLogger` のログは流れてしまい、ヘッダーと Body を通信ごとに分けて読めない。

RFC の Analysis 7 のとおり、シリアライズ前のメッセージは `CallInvoker` で見られる。Monitor を保留していた「protobuf を記録する前提」という理由は、もう無い。残っている仕事は次の 3 つ。

1. MessagePack の値を、項目名の付いた JSON にする
2. 記録の部品をプレイヤーのビルドから外す。`Client` は開発用のアセンブリを参照できないので、`ApiChannel` に Interceptor を外から渡す口(`SetInterceptor`)を足す
3. `Editor` の asmdef を足す(今のパッケージには無い)。記録の部品もここに置く

## Goals

*   `ApiChannel.CreateCallInvoker` で作った `CallInvoker` を通る全部の通信を記録する。`ApiCore` を通る通信、`IDeviceService` のトークン発行、それ以外(Sandbox の Server Clock ウィンドウなど)
*   基盤が渡したヘッダー・締め切りと、受け取ったヘッダー・トレーラーをそのまま見られる
*   送り直しは、送った回数だけ別の行になる。冪等性の対象の RPC は、同じ `x-request-id` で並ぶ
*   Body は項目名の付いた JSON で見られる。JSON への変換は、通信の流れの中では行わない
*   Body は通信の時点の中身を見られる。アプリが後からオブジェクトを書き換えても変わらない
*   エラーのときは、ステータスと `ErrorInfo`(reason・domain・metadata)を見られる
*   ウィンドウの構成と操作は gRPC 版と同じにする。違いは Technical Plan の 7 の表のとおり
*   記録の処理の失敗が、通信の結果に影響しない
*   プレイヤーのビルド(開発ビルドを含む)には、記録の部品がアセンブリごと入らない
*   アプリは何も書かなくても記録される

## Non-Goals

*   USB の実機(PlayerConnection)の表示。`ApiTraceSender`・`ApiTraceMessages`・端末の選択は作らない。そのための備えも入れない。足すときに、部品を `Runtime/` へ移して直す
*   Monitor からリクエストを送り直す・書き換えること。見るだけ(gRPC 版と同じ)
*   トークンや `secretKey` を隠すこと(Q3)
*   Body を grpcurl や Postman に貼って使えること。RFC のとおり、MessagePack ではこの利点は無くなる
*   複数の引数の RPC で、引数の名前を出すこと。通信の層には名前が残らない(Q2)
*   記録をファイルに残すこと。メモリーだけ(gRPC 版と同じ)
*   IL2CPP で確かめること(設計書 1 の Q8)。Editor だけの機能なので、リフレクションが IL2CPP で動く必要も今回は無い
*   StreamingHub の通信。基盤は Unary だけを使う
*   実サーバーの計測やログ(設計書 2)

## Open Questions

未解決の判断は無い。前提 A1〜A4 は Unity で確かめた(「実装の前に確かめる項目」)。

### 解決済み

*   **Q1(解決)**: 範囲は Editor だけ。USB の実機の表示は後の段階に分ける。RFC の Goal を直す(2026-11-07)
*   **Q2(解決)**: Body は項目名を出す。通信の時点でバイト列にして持ち、読まれるときに戻して、リフレクションで `[Key]` のメンバーをたどる。JSON にできなければ `MessagePackSerializer.ConvertToJson` の配列を出す(2026-11-07)。レビューの後に、記録の時点で JSON にする案と比べ、今の形を選び直した(2026-11-08。Alternative F2)
*   **Q3(解決)**: トークンや `secretKey` は隠さない。送ったままを出す。プレイヤーに入らず、Editor のメモリーにだけ持つことを守りにする(2026-11-07)
*   **Q4(解決)**: ウィンドウの構成は gRPC 版とまったく同じにする。実機の分を除くことと、Goal のために足す締め切りの表示の差は、Technical Plan の 7 の表に書く(2026-11-07)
*   **Q5(解決)**: アセンブリは `Editor` の 1 つにまとめる。記録の部品も `Editor/` に置く。実機を足すときに `Runtime/` へ移して分ける(2026-11-08)
*   **Q6(解決)**: JSON のテキストは自前で書かない。値を辞書・リスト・葉の木に変え、MessagePack にしてから `ConvertToJson` に通す(2026-11-08)

## Critical User Journeys (CUJs)

手順の前提は[サンプルの設計書の「動かし方」](sandbox-gacha-date-sample.md#動かし方)のとおり(Docker を起動し、`dotnet run --project samples/VolTech.Sandbox.Server --no-launch-profile` でサンプルサーバーを動かす)。サンプルは Play のたびに新しいプレイヤーになる。

### CUJ 1: 通信を見る

*   **Given**: Sandbox の `Assets/Sample/Sample.unity` を Play し、「はじめる」でホームに入った。Monitor のウィンドウを開いている
*   **When**: 「引く(300 ジェム)」を押す
*   **Then**: 一覧に `DrawAsync` の行が足され、結果が届くと `OK` と所要時間が出る(`Pending` の間は 200ms の更新より短いことがあるので、見えるとは限らない)。その行を選ぶと、詳細の上の行に締め切りが見え、Request のヘッダーに `authorization`・`x-request-id` が見え、Body に `{"UseFree": false}` の形の JSON が見える。Response の Body には `Common` と `CharacterName` が項目名付きで見える

### CUJ 2: トークンの発行を見る

*   **Given**: 新しく Play した(サンプルの端末情報はメモリーにだけあるので、まだ持っていない)
*   **When**: 「はじめる」を押す
*   **Then**: `GetHomeAsync` の前に、`IDeviceService` の `RegisterAsync`・`IssueTokenAsync` の行が並ぶ。`ApiCore` を通らない通信も見える

### CUJ 3: 再認証の送り直しを見る

*   **Given**: サーバーの時刻を +1 日進めた(トークンの期限が切れている)
*   **When**: 「引く(300 ジェム)」を押す
*   **Then**: `DrawAsync` の `Unauthenticated` の行、`IssueTokenAsync` の行、`DrawAsync` の `DATE_CHANGED` の行が順に並ぶ。2 つの `DrawAsync` の `x-request-id` は同じ(`DrawAsync` は冪等性の対象)

### CUJ 4: 業務エラーを見る

*   **Given**: 「引く(300 ジェム)」を 10 回押して、ジェムが 0 になった
*   **When**: 「引く(300 ジェム)」を押す
*   **Then**: 一覧の Result に `NOT_ENOUGH_GEMS` が出て、行の文字色が変わる。詳細の Error に、ステータスコード、detail、`ErrorInfo` の reason・domain が見える。metadata は 0 件と出る(サンプルの `NOT_ENOUGH_GEMS` は metadata を持たない)

### CUJ 5: Play を止めてから見返す

*   **Given**: Play 中に通信した
*   **When**: Play を止める
*   **Then**: 記録は残り、選んで読める。次に Play を始めると消える(Clear on Play。既定はオン)

### CUJ 6: プレイヤーのビルドに入らない

*   **Given**: プレイヤーのスクリプトをコンパイルする(Unity 7 では Development Build の有無でスクリプトのコンパイルは変わらない)
*   **When**: スクリプトをコンパイルする
*   **Then**: コンパイルが通り、`VolTech.MagicOnion.Client.Editor.dll` は出力に含まれない。`SetInterceptor` を呼ぶコードがプレイヤーに無いので、`CreateCallInvoker` は何も付けずに返す

## Technical Plan (Architecture)

### 全体の流れ

```text
アプリ: await gachaClient.DrawAsync(request).ToResult()
  → ApiCallFilter → ApiCore
    → ApiCallInvoker(試行ごとのヘッダー・締め切り・キャンセル)
      → ApiTraceInterceptor ← ここで記録する
        → GrpcChannel の CallInvoker → 通信

DeviceTokenProvider → IDeviceService のクライアント
      → ApiTraceInterceptor ← 同じ CallInvoker なので記録される

ApiTraceInterceptor → ApiTraceStore.Shared ← 200ms ごとに版を見る ← ApiMonitorWindow
```

`ApiChannel.CreateCallInvoker` の戻り値は、`ApiCore` と `IDeviceService` のクライアントの両方に渡している。ここに Interceptor を付ければ、両方の通信を 1 か所で記録できる。この戻り値を使わない経路は記録されない(Risks)。Interceptor は `ApiCallInvoker` より内側にあるので、`ApiCallInvoker` が付けたヘッダーと締め切りが見える。内部のヘッダー `x-voltech-call` は `ApiCallInvoker` が写さないので見えない。再送は試行ごとに `ApiCallInvoker` を通るので、送った回数だけ記録される。

### 1. アセンブリ

記録の部品もウィンドウも、新しい `VolTech.MagicOnion.Client.Editor`(`Editor/`、`includePlatforms` は `Editor`)に置く(Q5)。

| もの | 置き場所 | 入るところ |
| --- | --- | --- |
| `ApiTrace`・`ApiTraceStore`・`ApiTraceInterceptor`・`ApiTraceJson`・`ApiTraceMarshalling`・`ApiMonitorStartup`・`ApiMonitorWindow` | `Editor/` | Editor |
| `ApiChannel` の口 | `Runtime/Client/ApiChannel.cs` | 全部(付ける側が入らないので、プレイヤーでは何も付かない) |

*   `#if` は使わず、アセンブリで分ける(gRPC 版と同じ)
*   依存の向きは `Editor` → `Client`。`Client` は `Editor` を参照しない
*   名前空間は `VolTech.Client.Editor`(今のコードの `VolTech.Client` に合わせる)
*   `Client` の `AssemblyInfo.cs` の `InternalsVisibleTo` に `Editor` を足す。`ApiChannel` の口と `RpcErrorDetails`・`RpcErrorInfo` を使う
*   `Editor` の `AssemblyInfo.cs` で、EditMode のテストに internal を見せる

### 2. `ApiChannel` の口

```text
internal static void SetInterceptor(Interceptor interceptor)

public static CallInvoker CreateCallInvoker(GrpcChannel channel)
    invoker = channel.CreateCallInvoker()
    return interceptor == null ? invoker : invoker.Intercept(interceptor)
```

*   この口で、記録のためだけの static の可変なフィールドが 1 つ増える。`docs/designs/implementation/client.md` で `AddInterceptor` を外した理由は、このフィールドだった。Goal の「アプリは何も書かなくても記録される」のために受け入れる。`internal` なのでアプリからは呼べない
*   gRPC 版は Interceptor の一覧を持ち、RPC を呼ぶたびに一覧を読む。今回は Interceptor が 1 つで、`ApiMonitorStartup` が `[InitializeOnLoadMethod]` で Play より先に付ける。そのため、`CreateCallInvoker` のときに 1 回読むだけにする
*   Sandbox はドメインのリロードを切っている(`EnterPlayModeOptions` が 3)。static は Play をまたいで残る。同じ Interceptor を付け直しても動きは変わらない
*   Unity 7 のアナライザーは、static フィールドを持つ型に属性を求める(UAL0010)。static フィールドを持つ型(`ApiChannel`・`ApiTraceStore`・`ApiMonitorWindow`)には `[NoAutoStaticsCleanup]`(`Unity.Scripting.LifecycleManagement`)を付ける(`SampleClients` と同じ)。static class に付けられなければ、通常のクラスにする。`AutoStaticsCleanup` だと Play の区切りで消えるおそれがあり、Play を止めても見返せる動き(CUJ 5)が崩れる

### 3. 記録の処理(`ApiTraceInterceptor`)

`Grpc.Core.Interceptors.Interceptor` を継承し、`AsyncUnaryCall` だけを上書きする。処理は次のとおり。gRPC 版との違いは、手順 3 の同期の例外の記録と `ConfigureAwait(false)` の 2 つ。

1. `store.Recording` が false なら、何もせずに送る
2. 送信中の記録を作り、`ApiTraceStore.Add` で入れる。ここで例外が起きたら、警告のログ(`[ApiTrace]`)を出し、記録せずに送る
3. 送る。`continuation` が同期で例外を投げたら、下の表の例外の行のとおりに記録を `Failed` に更新して投げ直す
4. `AsyncUnaryCall` を同じ形で包み直す。レスポンスを待って同じ Id の記録を置き換える(`Update`)処理は `RecordAsync` に置く。記録は `ApiTrace.Clone` の写しに結果を入れて置き換え、一度入れた記録は書き換えない(ウィンドウが別のスレッドから読むため)
5. 結果と例外は、そのまま呼び出し側に返す。例外は包まずに投げ直す。`ApiCore` が読む `RpcException` の型は変わらない

`RecordAsync` と `TryGetResponseHeadersAsync` の `await` はすべて `ConfigureAwait(false)` にする。`ApiCallContext.ReadResponse` が包み直した `call` のレスポンスを `.Result` で読むから。`RecordAsync` が Unity の同期コンテキストに戻る形だと、`.Result` で止まったメインスレッドを `RecordAsync` が待って固まるおそれがある。同期コンテキストに戻らなければ、この経路で固まることは無い。あわせて、フレームを待つ時間が所要時間に入らないように、所要時間は `await call.ResponseAsync` の直後に取る。

記録の処理はスレッドプールで動く。`ApiTraceStore` は lock で守り、`Debug.LogWarning` は別のスレッドからも呼べる。

結果の埋め方は次のとおり。所要時間はどれも `Stopwatch` のミリ秒。

| 結果 | 状態 | 埋める項目 |
| --- | --- | --- |
| 成功 | `Succeeded` | レスポンスのヘッダー、Body、トレーラー、ステータス |
| `RpcException` | `Failed` | レスポンスのヘッダー(取れなければ空)、トレーラー、ステータスコード、detail、`ErrorInfo` |
| そのほかの例外 | `Failed` | ステータスコードに例外の型の名前、detail にメッセージ |

記録の処理で起きた例外は通信に混ぜず、`[ApiTrace]` の警告のログに出す。記録の項目は 6 節。

### 4. Body の持ち方(`ApiTraceMarshalling`)

gRPC 版は protobuf のメッセージの `Clone` で通信の時点の中身を残す。MagicOnion のメッセージはただの C# のオブジェクトなので `Clone` が無い。そこで、通信の流れの中でバイト列にして持ち、読まれるときに戻す。

```text
internal static byte[] Serialize<T>(Marshaller<T> marshaller, T value)
internal static Lazy<string> ToLazyJson<T>(Marshaller<T> marshaller, byte[] bytes)
    Value を取るとき: bytes を marshaller で戻す → ApiTraceJson.Format(value)
    例外なら:     MessagePackSerializer.ConvertToJson(bytes)(options は既定)
    それも例外なら: "(could not format as JSON: ...)"
```

*   シリアライズは `ContextualSerializer` で行う。MagicOnion の `Marshaller` の `Serializer` は `NotImplementedException` を投げる(2026-11-07 に確かめた)。`SerializationContext`(`GetBufferWriter`・`SetPayloadLength`・`Complete()`・`Complete(byte[])` を上書き)と `DeserializationContext`(`FakeCallInvoker` の中の実装と同じ形)を継承した小さなクラスが要る
*   リクエストは送る前、レスポンスは呼び出し側に返す前にシリアライズする。そのためレスポンスの処理にも `Method` が要る
*   実際に送ったバイト列を横取りするのではなく、もう 1 回シリアライズする。通信ごとに、リクエストとレスポンスで 1 回ずつ増える。Editor だけなので受け入れる
*   `Serialize` が例外を投げたら、Interceptor が捕まえて、Body を `(could not serialize: ...)` にする。通信は失敗させない
*   JSON にするのは `Lazy<string>` の `Value` を最初に取るとき(gRPC 版と同じ)で、詳細に Body を出すときがこれに当たる。この処理はメインスレッドで走る。Follow latest がオンなら、最新の行は結果が届くたびに変換する。検索は RPC の名前だけを見るので変換しない。一度も詳細に出ない記録は、バイト列のまま小さく、変換の費用も掛からない。`Lazy` は送信中の記録と結果の記録で共有する
*   記録の時点で JSON にする案は Alternative F2

### 5. JSON の作り方(`ApiTraceJson`)

`MessagePackSerializer.ConvertToJson` は `[Key(0)]` の型を配列で書く(`[true,[[3]]]`)。項目名を出すために、戻したオブジェクトをリフレクションでたどり、辞書(`Dictionary<string, object>`)・リスト(`List<object>`)・葉の木に変える。その木を `PrimitiveObjectResolver` で MessagePack にして、`ConvertToJson` に通す(2026-11-08 に確かめた。辞書の順が保たれ、エスケープも正しく、日本語は生のまま出る)。JSON のテキストは自前で書かない(Q6)。

```text
internal static string Format(object value)
```

上の行から順に判定する。表に無い型は例外を投げ、Body 全体が `ConvertToJson` の配列になる。リポジトリの通信の型で使う形だけを持ち、必要になったら行を足す。`ApiTraceJson` も `ApiTraceMarshalling` も static フィールドを持たない。木の MessagePack には `PrimitiveObjectResolver.Options` を使う。`Box<T>` と `DynamicArgumentTuple` はコンパイル時に型へ依存せず、型の完全名で判定してフィールドをリフレクションで読む。

| 値 | 木での形 |
| --- | --- |
| `null`、`Nil` | `null` |
| `string`、`bool`、整数 | そのまま |
| `float`、`double` | 有限ならそのまま。NaN と Infinity は `InvariantCulture` の文字列 |
| enum | 名前の文字列 |
| `DateTime` | `"o"` の文字列 |
| `byte[]` | base64 の文字列 |
| `Box<T>`(`MagicOnion.Internal`) | フィールド `Value` の中身 |
| `DynamicArgumentTuple<...>`(`MagicOnion`) | フィールド `Item1`・`Item2`… のリスト。引数の名前は通信の層に残らない |
| `[MessagePackObject]` の型(`[Key]` が int) | 辞書。キーはメンバー名 |
| `IDictionary` | 辞書。キーは `InvariantCulture` の文字列 |
| `IEnumerable` | リスト |

`[MessagePackObject]` の型のメンバーは、`[Key]` が付いたプロパティとフィールドで、基底クラスの分も含み、public でないものも見る。順は `IntKey` の小さい順。int の `[Key]` が 1 つも無い型は例外にする。文字列キーと `keyAsPropertyName` の型は MessagePack で map になるので、配列に切り替わっても名前が出る。

例: `SampleCommon` を含むレスポンスは次の形になる。ウィンドウが字下げして出す。

```json
{"Common":{"Gems":2700},"CharacterName":"アカリ","FreeDrawAvailable":true}
```

既定値の項目も出す。gRPC 版の `WithFormatDefaultValues(true)` と同じ。

### 6. 記録の項目と置き場所(`ApiTrace`・`ApiTraceStore`)

| 項目 | 中身 |
| --- | --- |
| Id | 連番。送信中の記録と、結果を埋めた記録は同じ Id |
| Method | `/ISampleGachaService/DrawAsync`(`context.Method.FullName`)。一覧の短い名前は最後の `/` より後 |
| 開始時刻 | 端末の時刻の ISO 8601 |
| 締め切り | UTC の ISO 8601。無ければ空 |
| 所要時間 | ミリ秒。送信中は -1 |
| 状態 | `Pending`・`Succeeded`・`Failed` |
| リクエスト | ヘッダー(送った順)、Body |
| レスポンス | ヘッダー、Body(`Lazy<string>`。エラーのときは空)、トレーラー |
| ステータス | コードの名前、detail |
| `ErrorInfo` | reason、domain、metadata。`RpcErrorDetails.GetErrorInfo` で読む。読めなければ reason に `(could not read ErrorInfo: ...)` を入れる。通信は失敗させない |

ヘッダーの `-bin` はバイト数と base64 で持つ。

古い順に持ち、500 件を超えたら古いものから捨てる。追加は `Add`、結果は `Update`(同じ Id が無ければ何もしない)。lock で守り、中身が変わるたびに版を増やす。ウィンドウは 200ms ごとに版を見る。別のスレッドから UI に触らないため。`Update` で Id が無いときに何もしないのは、送信中に消した記録が戻らないようにするため。

次は gRPC 版で実機の表示に使うものなので、写さない: `ApiTraceStore.Put`・`Changed`、`ApiTrace.RequestOmitted`・`Truncated`、`[Serializable]`。

### 7. ウィンドウ(`ApiMonitorWindow`・`ApiMonitorStartup`)

構成は gRPC 版と同じ(Q4)。`GrpcMonitorWindow.cs` を写し、実機に関わる参照(`TraceSource`・`source`・`connectionDropdown`・`connectionState`・`OnEnable`・`OnDisable`・`UpdateSourceControls`・`GrpcMonitorReceiver.Store`)をすべて消して、次を変える。

| 変えるところ | 中身 |
| --- | --- |
| 名前 | クラスは `ApiMonitorWindow`。メニューは `Window > VolTech MagicOnion > Monitor`。タブのタイトルは `Monitor` |
| 記録の置き場所 | `ApiTraceStore.Shared` に固定する |
| 締め切り | 詳細の上の行に、締め切りを出す(`deadline <UTC>`。空なら省く) |
| Clear on Play | 設定は `ApiMonitorStartup.ClearOnPlay` |
| Body | `BodySection(string body)`。`Truncated` の分岐を消す |
| 同じ通信の判定 | `ShowDetail` は同じ通信かを Id だけで判定する。gRPC 版が開始時刻と RPC も見るのは実機の再起動への対応で、今回は要らない |

Toolbar(Clear・Record・Clear on Play・Follow latest・検索)、`TwoPaneSplitView`、一覧の 4 列(Time・API・Result・Duration)、Result の文言(`Pending`・`OK`・エラーの reason かステータス)、詳細の Foldout(Request・Response・Error)、Headers の 1 行ずつの表示、Body の `TextField` の 64K 文字の上限、Copy、`IndentJson`(`ConvertToJson` は字下げしないので要る)は変えない。

`ApiMonitorStartup` は `[InitializeOnLoadMethod]` で `ApiChannel.SetInterceptor(new ApiTraceInterceptor(ApiTraceStore.Shared))`(`ApiChannel` の名前空間は `VolTech.Client.Unity`)を呼び、Clear on Play を持つ。Clear on Play がオンなら、`ExitingEditMode` のときに記録を消す。Play を始めた後に消すと、最初のシーンの初期化で送った通信の記録まで消えるため。設定は `EditorPrefs` に持つので、ウィンドウを閉じていても効く。

### 変えないもの

クライアントとの取り決め(ヘッダー、エラー、冪等性)、`ApiCore`・`ApiCallInvoker`・`ApiCallFilter`、アプリの公開 API、サーバー。`ApiChannel.CreateCallInvoker` のシグネチャも変えない。

## Alternatives Considered

### 記録の場所

#### Alternative A: `ApiCallInvoker` と `IDeviceService` の経路の 2 か所で記録する

*   **Pros**: `ApiChannel` に開発用の口が要らない
*   **Cons**: 入口が 2 つになる。アプリが作る `deviceClient` にも手を入れる。`ApiCore` を通らない通信(Server Clock ウィンドウなど)は記録されない
*   **Reason for Rejection**: 全部の通信を 1 か所で通せる `CallInvoker` のほうが、部品も漏れも少ない

#### Alternative B: `IClientFilter` で記録する

*   **Pros**: MagicOnion の公式の拡張点。`Metadata` の使い回しに触らない
*   **Cons**: フィルターはレスポンスの型を知らず、`GetResponseAs<object>()` は null を返す(設計書 1)。ヘッダーは `ApiCallInvoker` で付くので、フィルターからは実際のヘッダーが見えない。`IDeviceService` はフィルターを通らない
*   **Reason for Rejection**: Body も実際のヘッダーも読めない

### `ApiChannel` の口

#### Alternative C: gRPC 版と同じく、RPC を呼ぶたびに Interceptor の一覧を読む

*   **Pros**: `CallInvoker` を作った後に足された Interceptor も付く
*   **Cons**: RPC ごとに一覧を読む `CallInvoker` の型を新しく作る必要がある。付けるのは 1 つで、Play より先に付く
*   **Reason for Rejection**: 登録の順番の心配が無いので要らない

### プレイヤーのビルドからの外し方(Q5)

#### Alternative D: `#if UNITY_EDITOR` で分ける

*   **Pros**: asmdef が増えない
*   **Cons**: `Client` の中に開発用のコードが混ざる。プレイヤーのコンパイルで壊れても Editor では気づかない
*   **Reason for Rejection**: アセンブリごと外すほうが、入っていないことを確かめやすい(gRPC 版と同じ)

#### Alternative E: gRPC 版と同じく開発ビルドにも入れる

*   **Pros**: asmdef の条件が gRPC 版と同じ。実機を足すときの変更が少ない
*   **Cons**: 実機の表示が無いので、記録を読む人がいない。通信ごとのシリアライズと、トークンのメモリー上の滞留だけが増える。さらに、Unity 7 の `DEVELOPMENT_BUILD` のシンボルは定義されなくなった(2026-11-08 に `ScriptCompilationOptions.DevelopmentBuild` の非推奨のメッセージで確かめた)ので、gRPC 版と同じ条件の asmdef は開発ビルドでもコンパイルされない
*   **Reason for Rejection**: 読めない記録を開発ビルドで取る理由が無い。実機を足すときは、開発ビルドだけに入れる方法から決め直す

#### Alternative N: 記録の部品を別のアセンブリに分ける

*   **Pros**: `Runtime/Tracing/` に `UNITY_EDITOR` の条件で置けば、実機を足すときは条件を 1 つ足すだけで済む。通信の層と UI の境界がアセンブリで分かる
*   **Cons**: 今は分ける必要の無い asmdef と `AssemblyInfo` が増える。実機を作らなければ無駄になる
*   **Reason for Rejection**: 将来のための分割なので、オーナーは 1 つにまとめることを選んだ(Q5)

### Body の中身の残し方(Q2)

#### Alternative F: オブジェクトの参照をそのまま持つ

*   **Pros**: シリアライズが増えない
*   **Cons**: アプリが後から書き換えると、記録も変わる。gRPC 版が守っている「通信の時点の中身」を守れない
*   **Reason for Rejection**: 調べるためのツールで、記録が事実と違うと困る

#### Alternative F2: 記録の時点で JSON にする

*   **Pros**: `ApiTraceMarshalling`、2 つの Context、`Lazy`、前提 A2・A4、`Marshaller` のリスクが消える。文字列なので通信の時点の中身のまま残る
*   **Cons**: 通信ごとにリフレクションの走査が走る。読まない JSON も 500 件持つ。失敗のときに配列へ切り替えられない。巨大な Body では Editor が止まることがある(未測定)
*   **Reason for Rejection**: オーナーは、詳細に出すときに JSON にする形を残すことを選んだ(2026-11-08)。この案は設計レビューで指摘された。変換を後にする根拠は測定ではない。詳細に出ない記録を小さく保つことと、表に無い型を配列で見せられることが根拠

#### Alternative G: `Marshaller` を包んで、実際に送ったバイト列を取る

*   **Pros**: シリアライズが増えない。Body が送受信したバイト列そのものになり、クライアントの型に無い項目も配列で見える
*   **Cons**: `Method` を呼び出しごとに作り直す。`SerializationContext` の中身を横取りするので、Grpc.Net.Client の中の流れに依存する
*   **Reason for Rejection**: 正確さでは勝るが、項目名を出すにはクライアントの型で読み直すので、表示の多くは同じになる。Grpc.Net.Client の中の流れへの依存を、開発用のツールのために持たない

### JSON の作り方(Q2・Q6)

#### Alternative H: `ConvertToJson` の配列のまま出す

*   **Pros**: コードが 1 行。実際のバイト列のとおりに出る
*   **Cons**: 項目名が出ない。読む人が `[Key]` の番号を型と照らし合わせる
*   **Reason for Rejection**: オーナーは項目名を出すことを選んだ。配列は失敗のときの代わりに使う

#### Alternative I: `ContractlessStandardResolver` で項目名付きにシリアライズする

*   **Pros**: `ConvertToJson` だけで名前が出る
*   **Cons**: 動的な生成に頼る。基盤の型はどれも `[MessagePackObject]` と `[Key]` を付けている
*   **Reason for Rejection**: 動的な生成に頼らない形で項目名を出せるので、確かめる価値が無い

#### Alternative J: ソースジェネレーターで JSON の変換を生成する

*   **Pros**: リフレクションが要らない
*   **Cons**: 生成物の配布と検査が要る。Editor だけのツールには大きすぎる
*   **Reason for Rejection**: 設計書 1 でコード生成はしないと決めた

#### Alternative O: JSON のテキストを自前で書く

*   **Pros**: 木を作らず、値から直接書ける
*   **Cons**: エスケープや数値の書式を持ち、テストも増える
*   **Reason for Rejection**: `ConvertToJson` に任せれば要らない(Q6)

#### Alternative P: `com.unity.nuget.newtonsoft-json` で書く

*   **Pros**: JSON の扱いが揃っている。字下げも賄え、`IndentJson` を写さずに済む
*   **Cons**: 基盤のパッケージに依存の宣言が増える。アプリのプロジェクトにも入る
*   **Reason for Rejection**: 新しい依存なしで書ける(Q6)

### ウィンドウの作り方(Q4)

#### Alternative Q: IMGUI か UXML で作り直す

*   **Pros**: IMGUI は Sandbox の Server Clock と揃う。UXML は見た目を分けられる
*   **Cons**: gRPC 版の 619 行の動作を確かめ直すことになる。IMGUI は 500 行の一覧と大きな Body に向かない
*   **Reason for Rejection**: オーナーは gRPC 版とまったく同じ構成を選んだ

#### Alternative R: 記録の置き場所のイベントでウィンドウに知らせる

*   **Pros**: 200ms ごとの確認が要らない
*   **Cons**: 別のスレッドからの通知をメインスレッドへ戻す処理が要る。版を見る形なら、たくさん来ても 1 回の描き直しで済む
*   **Reason for Rejection**: 版を見る形のほうが部品が少ない(gRPC 版と同じ)

### 秘密の表示(Q3)

#### Alternative K: `authorization` だけ隠す

*   **Pros**: 小さな変更で、一番目につく値を防げる
*   **Cons**: Body の `secretKey`・`AccessToken` は残る
*   **Reason for Rejection**: 隠したように見えて守れていない

#### Alternative L: ヘッダーも Body も隠す

*   **Pros**: 画面共有でも値が出ない
*   **Cons**: 隠す項目を属性で指定すると公開 API が増える。一覧で指定すると付け忘れで漏れる。`secretKey` は `Item2` になるので、RPC ごとの指定が要る
*   **Reason for Rejection**: Editor のメモリーにだけ持つので、部品を増やす理由にならない

### 範囲(Q1)

#### Alternative M: USB の実機の表示も今回作る

*   **Pros**: RFC の Goal のまま。gRPC 版と同じ
*   **Cons**: 部品が約 200 行増え、確認は手動。Body の JSON のリフレクションを IL2CPP で確かめないまま出すことになる(設計書 1 の Q8)。Uta が実機で見る必要が出るまで使わない
*   **Reason for Rejection**: オーナーは後の段階に分けることを選んだ

--------------------------------------------------------------------------------

## Non-Functional Requirements

### Security & Privacy

*   Monitor には `authorization`、`IssueTokenAsync` の `secretKey`、レスポンスの `AccessToken` がそのまま見える(Q3)。設計書 1 の NFR で残した「表示で隠すこと」は、隠さないと決めた
*   守りは 2 つ。プレイヤーのビルドにアセンブリごと入らないこと、記録を Editor のメモリーにだけ持つこと(ファイルに残さない、ネットワークに送らない)
*   Copy で JSON がクリップボードに入る。Editor から本番のサーバーにつなぐと、本番の値も見える。画面を共有するときは気を付ける。利用ガイドに書く

### Performance

*   Editor の通信ごとに、シリアライズがリクエストとレスポンスで 1 回ずつ増える。JSON にするのは読まれた分だけ
*   プレイヤーで増える処理は、`CreateCallInvoker` の null の判定 1 回だけ
*   JSON への変換はメインスレッドで走る。Follow latest がオンなら、ウィンドウを開いているだけで最新の行を変換する
*   記録は 500 件まで。Body のバイト列も一緒に持ち、バイト数の上限は持たない

### Rollout & Rollback

*   クライアントとの取り決めも公開 API も変えない。Uta は基盤の版を上げれば使える
*   戻すときは `Editor/` を消し、`ApiChannel` を戻す

### Risks

*   **Editor の中の gRPC は 2.61.0**: Editor では Burst が同梱する版が読み込まれる(`docs/index.md`)。Monitor は Editor だけなので、この版で動けばよい
*   **`Marshaller` の使い方が MagicOnion の内部に依存する**: `ContextualSerializer`・`ContextualDeserializer` は gRPC の公開 API だが、動くかどうかは MagicOnion の作りに依存する。版を上げたら `ClientBehaviorTests` で確かめる
*   **`Box<T>`・`DynamicArgumentTuple` は MagicOnion の型**: `Box<T>` は `MagicOnion.Internal` にあり、版の更新で変わるおそれがある。変わっても JSON の形が崩れるだけで、通信には影響しない
*   **Body は受信したバイト列そのものではない**: クライアントの型で読み直した値なので、古い型は知らない項目を出さない。「サーバーが送っていない」と読み違えるおそれがある。デシリアライズに失敗した通信は Body が空になる。ヘッダーも `ApiCallInvoker` が渡した分だけで、HTTP の層で足されるものは見えない。調べるためのツールなので、利用ガイドにこの限界を書いて受け入れる
*   **表に無い型が 1 つでもあると、Body 全体が配列になる**: 項目名が消える。Uta の型で起きたら、`ApiTraceJson` の表に行を足す
*   **独自のフォーマッターの型は、表示が送った形と違うことがある**: `[Key]` のメンバーをたどるので、フォーマッターが別の形で書く型は、実際のバイト列と一致しない。開発用の表示なので受け入れる
*   **大きな Body**: 変換の間は Editor が止まる。数 MB のレスポンスが続くと、バイト列の分のメモリーも増える。未測定。困ったら、件数を減らすか Follow latest を切る
*   **追加のシリアライズがアプリのコードを動かす**: `IMessagePackSerializationCallbackReceiver.OnBeforeSerialize` を実装した型では、クライアントで普段走らない処理が Editor でだけ走る。今のリポジトリに実装は無い
*   **ドメインのリロード**: Play に入るときにリロードするプロジェクトでは、Clear on Play を切っていても記録は消える。記録はメモリーにしかないので受け入れる
*   **記録されない経路**: `ApiChannel.CreateCallInvoker` を通さず `channel.CreateCallInvoker()` を直接呼ぶか、`GrpcChannel` をクライアントに渡すと、エラーにならず記録もされない。Sandbox の `Assets/Smoke/SmokeClient.cs` はこの形で、対象外。利用ガイドの手順は `ApiChannel` を使う形なので受け入れる
*   **通信の途中で Play を止める**: 結果が Clear の後に届くことがある。`Update` は同じ Id が無ければ何もしないので、古い記録は戻らない

--------------------------------------------------------------------------------

## Detailed Implementation Plan

### 実装の前に確かめる項目

動的クライアントでは確かめた(2026-11-07、MagicOnion.Client 7.11.0 と偽の `CallInvoker`。スクラッチのプログラムで、リポジトリには残していない)。Unity では、実装の最初に確かめる。結果は `docs/index.md` のチェックリストに書く。

| 項目 | 確かめること | 確かめ方 | 崩れたら |
| --- | --- | --- | --- |
| **A1** | ソースジェネレーターのクライアントでも、`TRequest` が参照型ならその型、引数なしが `Box<Nil>`、値型 1 つが `Box<T>`、複数の引数が `Box<DynamicArgumentTuple<...>>` になる | ファイル 20 のテスト | `ApiTraceJson` の表の行を足す |
| **A2** | ソースジェネレーターのクライアントでも、捕まえた `Marshaller` の `ContextualSerializer` と `ContextualDeserializer` で、A1 の 4 つの形を往復できる | ファイル 20 のテスト | 実装を止める。Alternative F2 への変更を設計書に書き、オーナーの合意を取る |
| **A3** | Editor の gRPC 2.61.0 で、`CallInvoker.Intercept` と `Interceptor` が使える | ファイル 20 のテスト | `CallInvoker` を直接継承して包む(`ApiCallInvoker` と同じ形) |
| **A4** | `System.Buffers.ArrayBufferWriter<byte>` を Unity のアセンブリから使える | ファイル 20 のコンパイル | `ApiTraceMarshalling` に小さな `IBufferWriter<byte>` を書く |

### 実装の順番

1.  **設計書を承認する**: オーナーが承認してから実装に入る。Status を Approved にし、一覧を移す(ファイル 1・2)
2.  **確かめる**: A1〜A4(ファイル 20)
3.  **ドキュメント**: 段階 2 の結果を設計書と `docs/index.md` に書く。`docs:` のコミットを先に置く(ファイル 3〜8)。段階 2 のコードは、そのあとにコミットする
4.  **アセンブリ**: asmdef と `AssemblyInfo`、`ApiChannel` の口(ファイル 9〜12)。ファイル 19 もここ。確認: コンパイルが通る
5.  **記録**: `ApiTrace`・`ApiTraceStore`・`ApiTraceMarshalling`・`ApiTraceJson`・`ApiTraceInterceptor`・`ApiMonitorStartup`(ファイル 13〜17)と、テスト(ファイル 21、22)。確認: EditMode が通る
6.  **ウィンドウ**: `ApiMonitorWindow`(ファイル 18)。確認: コンパイルが通る
7.  **全体の検証**: Verification Plan を実行し、結果と実装で分かったことを設計書と `docs/index.md` に戻す。`/egm:egm-drift-report` でずれを確かめる

実装は 1 本のブランチ(`feature/monitor`)で行う。Uta の変更は要らない。

### Target Files to Create/Modify

`Unity/` の下に足すファイルの `.meta` は Unity が作るので、ファイルと一緒にコミットする。下の一覧には個別に挙げない。パスの `<pkg>` は `Unity/com.chinpangx.voltech.magiconion`。

#### ドキュメント

#### 1. `docs/designs/api-monitor.md` (Create)

*   **Proposed Changes**: この設計書。段階 2 と 7 で結果を戻す

#### 2. `docs/designs/index.md` (Modify)

*   **Proposed Changes**: 下書きの間は「検討中」に載せ、承認で「承認済み」に移す。CLAUDE.md の手順 4

#### 3. `docs/designs/foundation-direction.md` (Modify)

*   **Proposed Changes**: Goal の Monitor の項目から「実機の通信の表示」を外し、後の段階に分けたことと、記録の場所をこの設計書で決めたことを書く。「進める順番」の Monitor の項目を合わせ、設計書 2・3・5 より先に進めたことを残す

#### 4. `docs/index.md` (Modify)

*   **Proposed Changes**: 「決めること」の Tracing の項目は、この設計書の下書きで `[x]` にした。承認で「(検討中)」を外す。表の Unity クライアントの行の「Tracing は移していない」を直す。チェックリストに A1〜A4 を足す

#### 5. `docs/designs/implementation/client.md` (Modify)

*   **Proposed Changes**: 表の Tracing の行(要確認)と「移していないもの」の Tracing の行を、この設計書へのリンクに置き換える。`ApiChannel` の行を、口を 1 つ戻した形に直す。UAL0010 の段落に `[NoAutoStaticsCleanup]` を付けた型を足す。「テスト」の節の `ApiTraceTests` の行を直す

#### 6. `docs/designs/implementation/project-structure.md` (Modify)

*   **Proposed Changes**: ツリーに `Editor/` を足す。「Unity から参照するのは `Abstractions` と `Runtime/Client` だけ」に、開発用の `Editor` のアセンブリのことを足す

#### 7. `docs/guide/client.md`・`docs/guide/index.md` (Modify)

*   **Proposed Changes**: `docs/guide/client.md` に Monitor の節を足す(開き方、見えるもの、トークンと本番の値が見えることの注意、`ApiChannel.CreateCallInvoker` を通すこと、Body は受信したバイト列そのものではないこと)。「まだ無い」の一覧から gRPC Monitor を消す。`docs/guide/index.md` の「Unity 用の部品はまだ無い」の記述も合わせる

#### 8. `docs/spec/index.md` (Modify)

*   **Proposed Changes**: 「運用のための機能は落とし」を、ログと Monitor を含める形に直す。RFC の解決済みの 1

#### パッケージ

#### 9. `<pkg>/Runtime/Client/ApiChannel.cs` (Modify)

*   **Proposed Changes**: `internal static void SetInterceptor(Interceptor)` を足し、`CreateCallInvoker` で付ける。クラスに `[NoAutoStaticsCleanup]` を付ける。「Tracing は移していない」のコメントを直す。`CreateCallInvoker` のシグネチャは変えない

#### 10. `<pkg>/Runtime/Client/AssemblyInfo.cs` (Modify)

*   **Proposed Changes**: `InternalsVisibleTo("VolTech.MagicOnion.Client.Editor")` を足す

#### 11. `<pkg>/Editor/VolTech.MagicOnion.Client.Editor.asmdef` (Create)

*   **Proposed Changes**: `rootNamespace` は `VolTech.Client.Editor`。`references` は `VolTech.MagicOnion.Client`・`VolTech.Server.Abstractions`。`includePlatforms: ["Editor"]`。`autoReferenced: false`。MessagePack と Grpc の DLL は `overrideReferences: false` で自動で参照する

#### 12. `<pkg>/Editor/AssemblyInfo.cs` (Create)

*   **Proposed Changes**: `VolTech.MagicOnion.Client.Tests.EditMode` に internal を見せる

#### 13. `<pkg>/Editor/ApiTrace.cs`・`ApiTraceStore.cs` (Create)

*   **Proposed Changes**: gRPC 版を写す。Technical Plan の 6 のとおり、実機の分を除く。`ApiTraceStore.Shared` があるので `[NoAutoStaticsCleanup]` を付ける

#### 14. `<pkg>/Editor/ApiTraceMarshalling.cs` (Create)

*   **Proposed Changes**: `Marshaller<T>` でのバイト列との変換。`Serialize` と `ToLazyJson`、`SerializationContext`・`DeserializationContext` の小さな実装を持つ。Technical Plan の 4

#### 15. `<pkg>/Editor/ApiTraceJson.cs` (Create)

*   **Proposed Changes**: Technical Plan の 5 のとおり、値を木に変えて JSON の文字列にする

#### 16. `<pkg>/Editor/ApiTraceInterceptor.cs` (Create)

*   **Proposed Changes**: Technical Plan の 3 のとおり。gRPC 版の `ToLazyJson` を消して `ApiTraceMarshalling` を呼び、`RecordAsync` に `Method` を渡す

#### 17. `<pkg>/Editor/ApiMonitorStartup.cs` (Create)

*   **Proposed Changes**: Technical Plan の 7 のとおり。`ClearOnPlay`・`OnPlayModeStateChanged` は `GrpcMonitorReceiver.cs` を写す。`EditorPrefs` のキーは `VolTech.MagicOnion.Client.Monitor.ClearOnPlay`

#### 18. `<pkg>/Editor/ApiMonitorWindow.cs` (Create)

*   **Proposed Changes**: `GrpcMonitorWindow.cs` を写す。Technical Plan の 7 のとおり。`[NoAutoStaticsCleanup]` を付ける。protobuf と実機に触れたコメントも直す

#### テスト

#### 19. `<pkg>/Tests/EditMode/VolTech.MagicOnion.Client.Tests.EditMode.asmdef` (Modify)

*   **Proposed Changes**: `references` に `VolTech.MagicOnion.Client.Editor` を足す。`precompiledReferences` に `Grpc.Net.Client.dll` を足す(`ApiChannel` のテストが `GrpcChannel` を扱う)

#### 20. `<pkg>/Tests/EditMode/ClientBehaviorTests.cs` (Modify)

*   **Proposed Changes**: A1〜A3 のテストを足す。`IClientBehaviorService` に、参照型のリクエストを取るメソッドと複数の引数のメソッドを足し、`ClientBehaviorRequest` の型を足す。`TRequest` と `Marshaller` は、テストの中の `Interceptor` で捕まえる(`FakeCallInvoker` は変えない)。A2 のための `SerializationContext`・`DeserializationContext` は、テストの private なクラスとして書く。MagicOnion の版を上げたときに、前提が崩れていないかを見るために残す

#### 21. `<pkg>/Tests/EditMode/ApiTraceTests.cs` (Create)

*   **Proposed Changes**: 下の Verification Plan の記録のテスト。`ApiCore` を組み立てる偽物(`ITokenProvider`・`ICommonErrorHandler`)は、このファイルの private なクラスとして書く

#### 22. `<pkg>/Tests/EditMode/ApiTraceJsonTests.cs` (Create)

*   **Proposed Changes**: 下の Verification Plan の JSON のテスト

### Verification Plan

Editor が開いているときは、`unity test` ではなく、`unity command test run --mode <mode> --async_tests --caller plugin --skill unity-cli --project-path Unity/VolTech.Sandbox` で動かし、`unity command test status` で結果を見る。

**EditMode: `ApiTraceTests`**(本物の `MagicOnionClient` と、`FakeCallInvoker` を `Intercept` で包んだもの)

| テスト | 確かめること |
| --- | --- |
| `ApiTraceStore` の上限 | 500 件を超えると古いものから捨てる |
| `ApiTraceStore` の更新 | 更新で版が増える。消した記録が結果の置き換えで戻らない |
| 成功 | ヘッダー、Body の JSON、ステータス、所要時間が記録される。結果がそのまま返る |
| 業務エラー | ステータス、detail、`ErrorInfo` の reason・domain・metadata が記録される。呼び出し側に同じ型の `RpcException` が届く |
| 通信エラー | `ErrorInfo` が無くても記録される |
| 送信中 | 応答の前に `Pending` の記録が見える |
| 同期コンテキスト | `SynchronizationContext.Current` がある状態で、`TaskCompletionSource`(`RunContinuationsAsynchronously`)の `AsyncUnaryCall` を返す `continuation` を渡して Interceptor を呼ぶ。`Task.Run` で完了させ、メインスレッドの `call.ResponseAsync.Wait(5 秒)` が true になる。`FakeCallInvoker` は使わない |
| 通信の時点の中身 | 通信の後にリクエストとレスポンスのオブジェクトを書き換えても、Body の JSON は変わらない |
| 同期の例外 | `continuation` が同期で例外を投げると、記録が `Failed` になり、例外がそのまま届く |
| 記録の失敗 | 例外を投げる `Marshaller` の `Method` を `Interceptor` に直接渡しても、通信の結果は変わらず、Body が `(could not serialize: ...)` になる |
| 記録の一時停止 | `Recording` が false なら記録しない |
| 再送 | `ApiCore` の再認証で、冪等性の対象の RPC が 2 行になり、`x-request-id` が同じ |
| `ApiChannel` | `ApiChannel.CreateCallInvoker` で作った `CallInvoker` の通信が記録される。`ApiChannel.Create("http://127.0.0.1:1", 1 秒)` のチャンネルで `IClientBehaviorService` を呼び、`ApiTraceStore.Shared` に、呼ぶ前より新しい Id の失敗の記録ができることで確かめる。`ApiMonitorStartup` が付けた Interceptor を使う。テストの間は `Recording` を true にして、後で戻す。記録の確認は短いタイムアウトで待つ |

**EditMode: `ApiTraceJsonTests`**

`ApiTraceJson.Format` を、Technical Plan の 5 の表の行ごとに 1 件。あわせて、文字列のエスケープ、入れ子、メンバーの順、基底クラスのメンバー、NaN を確かめる。表に無い型を含む値で、`ToLazyJson` が配列に切り替わることを確かめる。戻せないバイト列でも配列になること、JSON にもできないバイト列で `(could not format as JSON: ...)` になることを確かめる。

**プレイヤーのビルド**: `PlayerBuildInterface.CompilePlayerScripts` を、開いている Editor で使い捨ての C# として実行する(リポジトリに残さない)。Windows 向けにコンパイルし、コンパイルが通り、出力に `VolTech.MagicOnion.Client.Editor.dll` が無いことを確かめる(CUJ 6)。Unity 7 では `ScriptCompilationOptions.DevelopmentBuild` が使えないので、1 回で足りる。

**手で確かめる**: CUJ の前提のとおりサンプルサーバーを起動し、CUJ 1〜5 を通す。開いている Editor は Unity Pipeline(`unity command` で Editor を操作する仕組み)で操作する。ウィンドウはテストで触らないので、ここが唯一の確認になる。あわせて次を確かめる。

*   Server Clock ウィンドウの通信も一覧に出る
*   Record を切ると記録されない。Clear on Play を切ると、次の Play でも残る
*   Follow latest、検索、Copy が動く
*   Play していないときの EditMode のテストの通信も記録される

**既存のテスト**: EditMode の全件、E2E の PlayMode 12 件(相手は `tests/VolTech.E2E.Server`)、`SampleFlowTests` 4 件(相手はサンプルサーバー)が通ること。パッケージのコードに UAL0010 の警告が出ないこと。

### 実装で確かめた結果

2026-11-08 に、7000.0.0a12 の Editor で確かめた。

*   **A1〜A4**: 成り立った。`ClientBehaviorTests` の 20 件が通った(ソースジェネレーターのクライアント、gRPC 2.61.0)。`ArrayBufferWriter<byte>` はそのまま使えた
*   **EditMode**: 120 件すべて通った(`ApiTraceTests` 13 件、`ApiTraceJsonTests` 12 件を含む)
*   **PlayMode**: E2E 12 件、`SampleFlowTests` 4 件が、Interceptor を付けた本物の通信で通った
*   **プレイヤーのコンパイル**: Windows 向けで通り、35 個の出力に `VolTech.MagicOnion.Client.Editor.dll` は無く、`VolTech.MagicOnion.Client.dll` はあった
*   **CUJ 1〜5**: 開いている Editor を Unity Pipeline で操作して通した。ボタンは `Button.onClick` を C# で呼び、ウィンドウの Toolbar はトグルの値を C# で変えた。ウィンドウの表示はスクリーンショットで確かめた。Record、検索、Copy、Clear on Play、Server Clock の通信も確かめた。OS の本物のマウスでは押していない。Follow latest を切った状態と Clear ボタンは動かしていない
*   **UAL0010**: パッケージのコードに警告は出なかった

実装で分かったことは次のとおり。

*   `[NoAutoStaticsCleanup]` は static class にも付けられた
*   `ApiMonitorStartup` は static フィールドを持たないので、属性は要らない
*   単一の参照型の引数の RPC(`RegisterAsync(string)`)は、Body が `"..."` の文字列になる
*   Unity 7 の `DEVELOPMENT_BUILD` は定義されない(Alternative E)
