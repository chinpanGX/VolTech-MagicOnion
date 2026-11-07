# Sandbox のガチャと日付更新のサンプル

*   **Author:** ちんぱん
*   **Status:** Approved
*   **Date:** 2026-11-07

--------------------------------------------------------------------------------

## Overview

Sandbox(開発用の Unity プロジェクト `Unity/VolTech.Sandbox`)に、ガチャを引く画面と、サーバーの時刻を進める Editor のウィンドウを作る。基盤の使い方を、動くもので見られるようにする。

サンプルは 3 つでできている。

1. **サンプルサーバー**(`samples/VolTech.Sandbox.Server`): 基盤の上に作ったガチャのサーバー。ローカルで動かす
2. **ガチャの画面**(`Assets/Sample`): 残りのジェムと、ガチャのボタンがある Play モードの画面
3. **Server Clock ウィンドウ**(`Assets/Sample/Editor`): Play モード中に、サーバーの時刻を進める・元に戻す Editor のウィンドウ

基盤(`src/VolTech.Server`・`Unity/com.chinpangx.voltech.magiconion`)のコードは変えない。

この文書の前提は、[設計書 1(共通レスポンス項目とクライアントの呼び方)](common-response-and-client-filter.md)と、利用ガイドの[サーバー](../guide/server.md)・[クライアント](../guide/client.md)。

## The Problem

設計書 1 の実装で、基盤のアプリの書き方は決まった。ただ、それをひと続きで見られるものが無い。

*   利用ガイドには断片のコードしか無い。`tests/VolTech.E2E.Server` は通信の確認のためのもので、ゲームの形をしていない
*   `DATE_CHANGED`(クライアントの持つゲーム内の日付が、サーバーの日付と違う)は、日付が変わる時刻に居合わせないと見られない。E2E のテストにも入っていない
*   `ServiceContext.GetGameDate()` を使う業務の処理(1 日 1 回の無料など)の例が無い
*   動いているサーバーの時刻を外から変える手段が無い。`TestHost` は `FakeTimeProvider` に差し替えているが、テストの中だけで使う

## Goals

*   ガチャ 1 回の流れ(`ApiCore` の組み立て、RPC、`Common` の反映、業務エラー)を、Play モードで動かして見られる
*   Server Clock ウィンドウでサーバーの時刻を進めると、次の RPC が `DATE_CHANGED` になり、タイトルに戻る流れを見られる
*   1 日 1 回の無料ガチャが、日付が変わると引けるようになる
*   時刻の変更がサンプルサーバーの中で閉じる。基盤に開発用の API を足さない

## Non-Goals

*   運用の管理画面
*   Play モードの外で時刻を変えること
*   ガチャの抽選の作り込み(提供割合の表示、天井、マスターデータ)
*   見た目の作り込み
*   本番向けの時刻の変更。サンプルサーバーは `localhost` だけで待ち受ける
*   Unity Player のビルドで動かすこと。サンプルのコードは `UNITY_EDITOR` のときだけコンパイルする。Sandbox は IL2CPP の確認にも使うので、Player に入れない
*   CI でサンプルを動かすこと

## Open Questions

無し。

### 解決済み

*   **Q1: 画面を何で作るか。** uGUI をコードで組み立てる。Sandbox の Active Input Handling は Input System Package で、Play モードの `OnGUI` には入力が届かない。設定は変えない(2026-11-07)
*   **Q2: サーバーの起動の仕方。** `dotnet run` と Testcontainers。E2E のサーバーと同じで、データは止めると消える。Editor から起動するボタンは作らない(2026-11-07)

## Critical User Journeys (CUJs)

### 動かし方

1. Docker を起動する
2. `dotnet run --project samples/VolTech.Sandbox.Server --no-launch-profile` を実行する。PostgreSQL のコンテナも起動し、`http://localhost:5200` で待ち受ける
3. Sandbox で `Assets/Sample/Sample.unity` を開き、Play モードにする

サーバーを止めると、データと時刻のずれが消える。サーバーを動かしたまま Play をやめると、時刻のずれは残る。次の Play で「リセット」を押す。

クライアントの端末情報はメモリーにだけ持つので、Play のたびに新しいプレイヤーになり、ジェムも 3000 に戻る。**Play 中にサーバーを再起動したら、Play もやり直す。** DB が消えて、メモリーの端末情報が無効になるため。

### CUJ 1: ガチャを引く

新しく Play した状態から始める。

1. タイトルの「はじめる」を押す。ホームに 3000 ジェムと表示され、「無料で引く」「引く(300 ジェム)」のボタンが見える
2. 「引く(300 ジェム)」を押す。当たったキャラクターの名前が出て、2700 ジェムになる。「無料で引く」は押せるまま
3. 「引く(300 ジェム)」を 10 回押すと 0 ジェムになる。11 回目を押すと、画面の中に「ジェムが足りません」と出る。ボタンはジェムが足りなくても押せる。エラーの帯は出ない

### CUJ 2: 日付が変わる

新しく Play した状態で、ホームから始める。元の日付を D とする。

1. 「無料で引く」を押す。ジェムは減らず、ボタンが押せなくなる
2. **VolTech > Server Clock** のウィンドウを開く。サーバーの時刻とゲーム内の日付 D が見える
3. 「+1 日」を押す。ゲーム内の日付が D+1 になる
4. 「引く(300 ジェム)」を押す。+1 日はトークンの期限(既定で 1 時間)を超えるので、サーバーは先に `UNAUTHENTICATED` を返す。クライアントはトークンを取り直して 1 回だけ送り直し、次に `DATE_CHANGED` が返る
5. 帯に「日付が変わりました」と出る。「タイトルへ」を押すと、タイトルに戻る。ジェムは減っていない
6. 「はじめる」を押す。「無料で引く」が押せるようになっている
7. 「無料で引く」を押す。ジェムは減らず、ボタンが押せなくなる

### CUJ 3: 時刻を元に戻す

CUJ 2 の続きで、日付 D+1 のホームから始める。

1. 「リセット」を押す。ゲーム内の日付が D に戻る
2. 「引く(300 ジェム)」を押す。`DATE_CHANGED` が返り、帯の「タイトルへ」でタイトルに戻る
3. 「はじめる」を押す。最後に無料で引いた日は D+1 で、今日の D と違うので、「無料で引く」が押せる。開発用の操作なので、同じ日にもう一度引けることを許す

## Technical Plan (Architecture)

### 時刻を変える場所

時刻を変えるのは **サーバー** で、Editor のウィンドウは入口にすぎない。ゲーム内の日付はサーバーが決めるので、クライアントの時計を変えても `DATE_CHANGED` は起きない。

### 全体の流れ

```
[ガチャの画面] --- ApiCore ---------> ISampleGachaService ---> PostgreSQL
                                          |
                                     GameClock / AuthFilter
                                          |
                                     ShiftableTimeProvider
                                          ^
[Server Clock ウィンドウ] ----------> IServerClockService
```

### サーバーの起動

`tests/VolTech.E2E.Server/Program.cs` を写し、次を変える。Testcontainers での PostgreSQL の起動、`VoltechDatabase.Migrate`、`GameDay`(4 時、`Asia/Tokyo`)、`ActivePlayerResolver`(いつも `Active` を返す)は同じ。

*   `ShiftableTimeProvider` の登録を足す(次の節)
*   `VoltechDatabase.Migrate` の後に `sample_players` を作る
*   `AddCommonResponse<E2ECommon, E2ECollector>()` を `AddCommonResponse<SampleCommon, SampleCollector>()` に変える
*   `E2EState` の登録を消す
*   ポートは `ListenLocalhost(5200, ...)` に固定する。引数は読まない

### サーバーの時刻

`TimeProvider` を継承した `ShiftableTimeProvider` をサンプルサーバーに置く。`GetUtcNow` だけを上書きし、本当の時刻にずらす量を足して返す。ずらす量は `Interlocked` で読み書きする。

同じインスタンスを `ShiftableTimeProvider` と `TimeProvider` の 2 つの型で、`AddVoltechServer` より前に登録する。`AddVoltechServer` は `TryAddSingleton(TimeProvider.System)` で登録するので、これで差し替わる。基盤は変えない。

### 時刻の RPC

```csharp
[SkipAuth]
public interface IServerClockService : IService<IServerClockService>
{
    UnaryResult<ServerClockInfo> GetAsync();
    UnaryResult<ServerClockInfo> SetOffsetAsync(long seconds);
}
```

*   `[SkipAuth]` は共有インターフェースに付ける。実装の属性は読まれない。ウィンドウはプレイヤーではないので、トークンを持たない。`[SkipAuth]` の RPC は日付の判定もしないので、時刻を変えた後も呼べる
*   `SetOffsetAsync` は、本当の時刻からずらす秒数を絶対値で指定する。0 でリセットになる。[サーバーのガイド](../guide/server.md)の、副作用のある `[SkipAuth]` の RPC は何度呼んでも同じ結果にする、という決まりに合わせる
*   秒数は 0 以上 3650 日以下にする。範囲の外は `ReturnStatusException(StatusCode.InvalidArgument, ...)` を投げる(`DeviceService` と同じ)。過去へはずらさない
*   応答は `GameClock.UtcNow` と `GameClock.GetGameDate(now)` で作り、`UtcDateTime` と `yyyy-MM-dd` の文字列にする。`Context.GetGameDate()` はリクエストを受けたときに決めた日付なので、変更前の値になる

### ガチャの RPC

```csharp
public interface ISampleGachaService : IService<ISampleGachaService>
{
    [NoSideEffects]
    UnaryResult<HomeResponse> GetHomeAsync();
    UnaryResult<DrawResponse> DrawAsync(DrawRequest request);
}
```

どちらも `transactions.RunAsync` の中で処理する。

価格などはサーバーの `SampleGacha` に定数で置く。初期のジェムは 3000、1 回の価格は 300、キャラクターは「アカリ」「ヒナタ」「ソラ」の 3 つから一様に選ぶ。

`DrawAsync` の処理は次のとおり。

1. プレイヤーの行が無ければ作る(`INSERT ... ON CONFLICT DO NOTHING`)
2. `UseFree` が true のとき: `UPDATE sample_players SET last_free_draw = $today WHERE player_id = $player AND last_free_draw IS DISTINCT FROM $today`。0 行なら `FREE_DRAW_USED` を投げる。`tx.WriteGameLog(GameLog.Event("gacha", "free_draw"))` を書く。`FreeDrawAvailable` は false
3. `UseFree` が false のとき: `UPDATE sample_players SET gems = gems - 300 WHERE player_id = $player AND gems >= 300 RETURNING gems, last_free_draw IS DISTINCT FROM $today`。0 行なら `NOT_ENOUGH_GEMS` を投げる。`FreeDrawAvailable` は 2 つ目の列。`tx.WriteGameLog(GameLog.Quantity("gems", -300, after, "gacha_draw"))` を書き、`tx.GetCollector<SampleCollector>().Gems = after` で変更後のジェムを記録する
4. キャラクターを選び、`DrawResponse` を返す

SQL の `$today`(`Context.GetGameDate()`)と `$player`(`Ulid` の文字列)は説明のための名前で、実装は位置指定のパラメーターにする。

業務エラーを投げると、`RunAsync` がロールバックする。`DrawAsync` は冪等性の対象なので、同じ `x-request-id` の再送には保存済みのレスポンスが返り、ジェムは 2 回減らない。抽選の結果もレスポンスに入っているので同じものが返る。

`GetHomeAsync` は読み取り専用なので、行を作らない。行が無ければ 3000 ジェムで無料ガチャを引けるものとして返す。ホームは全件を返すので、`Common` を使わず、`HomeResponse` に `IApiResponse<>` を実装しない([仕様](../spec/common-response.md))。

### 共有の型

`Assets/Sample/Shared/` に置き、サンプルサーバーからソースをリンクする。Unity でコンパイルするので netstandard2.1 / C# 9 の範囲で書き、`[MessagePackObject]` と `[Key]` を付ける。`?` を使うファイルの先頭に `#nullable enable` を書く。

| 型 | フィールド |
|---|---|
| `SampleCommon` | `int Gems`(変更後のジェム) |
| `DrawRequest` | `bool UseFree` |
| `DrawResponse : IApiResponse<SampleCommon>` | `SampleCommon? Common`、`string CharacterName`、`bool FreeDrawAvailable` |
| `HomeResponse` | `int Gems`、`bool FreeDrawAvailable` |
| `ServerClockInfo` | `DateTime UtcNow`、`string GameDate`(`yyyy-MM-dd`)、`long OffsetSeconds` |

`SampleErrors` の domain は `sample`。`NotEnoughGems`(`NOT_ENOUGH_GEMS`)と `FreeDrawUsed`(`FREE_DRAW_USED`)を、`E2EErrors` と同じくプロパティで書く。

### データ

サンプルサーバーが起動時に、`VoltechDatabase.Migrate` の後で表を作る。データは止めると消えるので、DbUp は使わない。

```sql
CREATE TABLE IF NOT EXISTS sample_players (
    player_id        TEXT PRIMARY KEY,
    gems             INTEGER NOT NULL,
    last_free_draw   DATE
);
```

無料ガチャの判定は `last_free_draw IS DISTINCT FROM 今日`。先の日付で無料で引いてから時刻を戻すと、同じ日にもう一度引ける(CUJ 3)。開発用なので許す。

### クライアントの組み立て

`SampleSetup.Create` が `ApiCore` を組み立て、画面とテストの両方が使う。組み立ての間違いをテストで見つけられる。

```csharp
public static SampleSession Create(string address, IErrorDialogView view, IErrorNavigator navigator);

public sealed class SampleSession : IDisposable
{
    public SamplePlayer Player { get; }
    public UniTask<ApiResult<HomeResponse>> EnterAsync();
    public UniTask<ApiResult<DrawResponse>> DrawAsync(bool useFree);
    public void Dispose();
}
```

*   `Create` は `ApiChannel.Create` でチャンネルを作り、`E2ETests.SetUp` と同じ手順で `ApiCoreOptions`(`AppVersion.Windows`、版は 1)、`DeviceTokenProvider`、`ApiCore` を作る。`Dispose` がチャンネルを閉じる
*   `EnterAsync` は `GetHomeAsync` を呼び、成功したら `Player.ReplaceAll(response)` で全件を置き換える
*   `DrawAsync` は `WithHandledCodes(SampleErrors.NotEnoughGems, SampleErrors.FreeDrawUsed)` を付けて呼び、成功したら `Player.FreeDrawAvailable` を更新する
*   `SamplePlayer` は `Gems` と `FreeDrawAvailable` を持つ。`SampleSession` が持ち、static にしない。Sandbox はドメインのリロードを切っているので、static は Play をまたいで残る

組み立てに使う部品は次のとおり。

| 部品 | 使うもの |
|---|---|
| 端末情報 | `InMemoryAccountStore`。テスト用の型だが、DB が止めると消えるサンプルでは、ファイルに残すと再起動の後に認証が失敗し続けるので、これを使う |
| エラーの一覧 | `ErrorPolicyProvider` に `NotEnoughGems`・`FreeDrawUsed` を `ErrorAction.Stay` で渡す。一覧に無いコードは `UNKNOWN_ERROR` になる。`ErrorDefinition` はクライアントの型なので、一覧は `SampleSetup` に置く |
| 共通エラーハンドラー | `DefaultCommonErrorHandler` をそのまま使う |
| エラーの見た目 | `view`。画面は `SampleErrorView`、テストは偽物 |
| 画面の遷移 | `navigator`。`WithErrorNavigator` で渡す。画面は `SampleNavigator`、テストは偽物 |
| `Common` の反映 | `SampleCommonApplier : ICommonResponseApplier<SampleCommon>`。`SamplePlayer.Gems` を書き換える |

共通エラーハンドラーは画面を動かさない。`DATE_CHANGED` の action は `Title` なので、ハンドラーが `ErrorDecision.ReturnToTitle` を返し、`ApiCore` が `ResetSession` でトークンと日付を捨て、`GoToTitleAsync` を呼ぶ。

| エラー | action | 画面 |
|---|---|---|
| `NOT_ENOUGH_GEMS` | `Stay` | `WithHandledCodes` で指定するので帯は出ない。画面の中に「ジェムが足りません」 |
| `FREE_DRAW_USED` | `Stay` | 同じく、画面の中に「今日の無料ガチャは引きました」 |
| `DATE_CHANGED` | `Title` | 帯に「日付が変わりました」と「タイトルへ」 |
| `NETWORK_ERROR` | `Retry` | 帯に「サーバーに接続できません」と「再試行」「タイトルへ」 |
| そのほか | action のとおり | 帯に「エラーが発生しました」と、`ErrorDialogButtons` に合うボタン |

文言は画面が持つ。`ErrorMessage` はユーザーに見せない決まりなので使わない。

### ガチャの画面

`SampleGame`(MonoBehaviour)が、`Awake` で画面と `SampleSession` を作り、`OnDestroy` で `Dispose` する。

画面は `SampleUi` がコードで組み立てる。`Canvas`、`CanvasScaler`、`GraphicRaycaster`、`EventSystem`、`InputSystemUIInputModule`(`AssignDefaultActions` で既定の入力を割り当てる)と、`Text`・`Button` を作る。日本語を出すために、TextMeshPro ではなく `Text` に組み込みの `LegacyRuntime.ttf` を使う。文字が出るかは実装の最初に確かめる。

状態はタイトルとホームの 2 つ。

*   **タイトル**: 「はじめる」のボタン。押すと `EnterAsync` を呼び、成功したらホームへ
*   **ホーム**: 残りのジェム、結果の文言、「無料で引く」「引く(300 ジェム)」のボタン。「無料で引く」は `FreeDrawAvailable` が false なら押せない。「引く」はジェムが足りなくても押せる

通信中は、どのボタンも押せない。

*   `SampleErrorView : IErrorDialogView` は帯の `GameObject` を持つ。`ShowAsync` で文言とボタンを出し、`onClick` で `UniTaskCompletionSource` を完了させて帯を隠す。`ErrorDialogButton` の 5 種類すべてにボタンの文言を持たせる
*   `SampleNavigator : IErrorNavigator` は、`SampleGame` からタイトルへ戻す `Action` を受け取る。`GoToHomeAsync`・`OpenStoreAsync` はログを出すだけ(このサンプルでは `Home` と `Update` の action は起きない)

### Server Clock ウィンドウ

`EditorWindow` を IMGUI で作り、メニューは **VolTech > Server Clock**。Editor の GUI なので、Input System の設定の影響を受けない。Play モード中だけ操作でき、それ以外のときは案内の文言を出す。

*   ボタンは「+1 日」「リセット」「更新」の 3 つ。通信中は押せない
*   「+1 日」は `GetAsync` の後に `SetOffsetAsync(今のずらす量 + 86400)` を呼ぶ。「リセット」は `SetOffsetAsync(0)` を呼ぶ。「更新」は `GetAsync` を呼ぶ
*   表示はサーバーの時刻(UTC)、ゲーム内の日付、ずらしている秒数。Play 中にウィンドウを開いたとき、`EnteredPlayMode` のとき、ボタンを押した後に更新する。Sandbox はドメインのリロードを切っているので、`OnEnable` は Play の開始では動かない。通信が終わったら `Repaint` を呼ぶ
*   通信のたびに `ApiChannel.Create` でチャンネルを作って閉じる。`ApiChannel` の案内は 1 つを使い回す形だが、ボタンを押したときにしか通信しないウィンドウでは、チャンネルの寿命を持たないほうが単純になる
*   `ApiCore` は通さず、`SampleClients.CreateClockClient(callInvoker)` で呼ぶ。トークンが要らないことと、ガチャの画面がエラーの帯で止まっていても時刻を変えられるようにするため。RPC ごとに `WithOptions` で 5 秒の締め切りを付ける
*   接続先は `SampleClients.Address`(`http://localhost:5200`)の定数。失敗したらウィンドウに失敗を表示する。自動で送り直さない

### 変えないもの

`src/VolTech.Server`・`Unity/com.chinpangx.voltech.magiconion`・`tests/VolTech.E2E.Server`・取り決め(ヘッダー、エラー)。

## Alternatives Considered

### サンプルの作り方

| 案 | 長所 | 短所 |
|---|---|---|
| **A. 新しいサンプルサーバーを作る(採用)** | ゲームの形で読める。失敗を起こすテスト用の RPC と混ざらない | サーバーが 2 つになる |
| B. `tests/VolTech.E2E.Server` に足す | 小さい | テストの RPC とガチャが混ざり、見本として読みにくい |
| C. 利用ガイドに通しの例を書くだけ | 保守がいらない | `DATE_CHANGED` を動かして見られない |

### 画面の作り方(Q1)

| 案 | 長所 | 短所 |
|---|---|---|
| **A. uGUI をコードで組み立てる(採用)** | Sandbox の設定を変えない。Prefab やアセットが増えない | Canvas と EventSystem の分だけコードが増える |
| B. IMGUI と Active Input Handling の Both | コードが一番短い | Sandbox 全体の設定を変える。`OnGUI` の途中で画面が変わらないよう気を付ける必要がある |
| C. UI Toolkit | 新しい | `PanelSettings` と UXML のアセットが増える |

### 時刻を変える場所

| 案 | 長所 | 短所 |
|---|---|---|
| **A. サーバーの `TimeProvider` を差し替える(採用)** | 基盤を変えない。認証の期限まで同じ時計で動く | PostgreSQL の `now()` は変わらない。基盤は記録の時刻にしか使っていない |
| B. 基盤に開発用の時刻変更 API を入れる | どのアプリでも使える | 本番に残る口が増える。サンプルで形を確かめてから決めればよい |

### 時刻を変える入口

| 案 | 長所 | 短所 |
|---|---|---|
| **A. Play モード中の Editor のウィンドウ + gRPC(採用)** | 追加の依存が無い。Play モード中の通信は E2E で確かめ済み。`[SkipAuth]` の使い方も見せられる | Play モード中しか使えない |
| B. Play モードの外でも使えるウィンドウ | 画面を動かさずに時刻を変えられる | どの CUJ でも使わない。Play モードの外の通信、ドメインリロード、チャンネルの寿命を確かめる必要がある |
| C. ガチャの画面の中のボタン | Editor の asmdef が要らない | 時刻を変える操作がゲームの画面に混ざる |
| D. 別ポートの HTTP | Web の入口を足せる | 基盤の待ち受けは h2c なので、別のポートと別の口を作ることになる |

## Non-Functional Requirements

### Security & Privacy

*   `IServerClockService` は認証なしで時刻を変えられる。サンプルサーバーにだけ置き、`localhost` だけで待ち受ける。これを本番のサーバーに写さないことを、`IServerClockService` のコメントと `docs/guide/index.md` に書く

### Rollout & Rollback

*   サンプルは基盤の配布物(UPM のパッケージ)に入らない。消しても基盤に影響しない

### Risks

*   **サンプルが黙って壊れる**: CI で動かないので、基盤の公開 API を変えても気づけない。ただし `samples/VolTech.Sandbox.Server` はソリューションに入るので、サーバー側の破壊は `dotnet build` で分かる。基盤の版を上げる前に、PlayMode のテストを手で動かす
*   **トークンの事前更新が効かない**: サーバーの時刻だけが進むので、クライアントは期限切れに気づかない。`UNAUTHENTICATED` を受けてから取り直す。CUJ 2 はこの経路を通る
*   **ずらしたままのサーバー**: テストが途中で落ちると時刻がずれたまま残る。テストの `UnitySetUp` と `UnityTearDown` の両方で `SetOffsetAsync(0)` を呼ぶ

## Detailed Implementation Plan

### 実装の順番

1. この設計書の Status を Approved にし、`docs/designs/index.md` に載せ、`docs:` のコミットを置く
2. 共有の型、3 つの asmdef、サーバーの csproj、最小の `Program.cs`、`.slnx` の変更。確認: `dotnet build` が通り、Unity でコンパイルエラーが出ない
3. 確かめる項目: `Sample.unity` に `SampleGame` の空の MonoBehaviour を付けられる。`Tests` に置いた空のテストが `--mode PlayMode --filter VolTech.Sandbox.Sample` で 1 件になる(0 件ではない)。`LegacyRuntime.ttf` の `Text` で日本語が出る。結果は `docs/index.md` のチェックリストに書く
4. サーバー: `ShiftableTimeProvider`、`IServerClockService`、起動。確認: `dotnet run` で起動する
5. サーバー: `ISampleGachaService`、表、コレクター。確認: `dotnet build`
6. Unity: `SampleClients`、`SampleSetup`、`SampleSession`、`SamplePlayer`、`SampleCommonApplier`。確認: コンパイルエラーが出ない
7. Unity: PlayMode のテスト。確認: サーバーとクライアントの流れが通る
8. Unity: `SampleUi`、`SampleErrorView`、`SampleNavigator`、`SampleGame`。確認: コンパイルエラーが出ない
9. Unity: Server Clock ウィンドウ。確認: コンパイルエラーが出ない
10. CUJ 1〜3 と、下の「手で確かめる」を Editor で確かめる
11. 文書: `README.md`、`docs/guide/index.md`、`docs/designs/implementation/project-structure.md`、`docs/index.md` を更新する。実装で分かったことをこの設計書に戻す。`docs:` のコミットをコードのコミットの前に置く。`/egm:egm-drift-report` でずれを確かめる

### Target Files to Create/Modify

**Create: `samples/VolTech.Sandbox.Server/`**

*   `VolTech.Sandbox.Server.csproj`: `tests/VolTech.E2E.Server` の csproj を写し、リンクを `../../Unity/VolTech.Sandbox/Assets/Sample/Shared/*.cs` にする
*   `Program.cs`: 「サーバーの起動」の節
*   `ShiftableTimeProvider.cs`
*   `ServerClockService.cs`
*   `SampleGachaService.cs`: `SampleGacha`(定数)もここに置く
*   `SampleCollector.cs`: `ICommonResponseCollector<SampleCommon>`。`Gems` が記録されていなければ null を返す
*   `ActivePlayerResolver.cs`

**Create: `Unity/VolTech.Sandbox/Assets/Sample/`**(`.meta` も含む)

*   `Shared/IServerClockService.cs`、`Shared/ISampleGachaService.cs`、`Shared/SampleTypes.cs`、`Shared/SampleErrors.cs`
*   `VolTech.Sandbox.Sample.asmdef`
*   `SampleClients.cs`: `E2EClients.cs` を写し、`ISampleGachaService`・`IDeviceService` を指定する。`Address` の定数と `CreateClockClient(CallInvoker)` もここに置く
*   `SampleSetup.cs`、`SampleSession.cs`、`SamplePlayer.cs`、`SampleCommonApplier.cs`
*   `SampleUi.cs`、`SampleErrorView.cs`、`SampleNavigator.cs`、`SampleGame.cs`
*   `Sample.unity`: `SampleGame` を付けた GameObject だけのシーン。Build Settings には入れない
*   `Editor/VolTech.Sandbox.Sample.Editor.asmdef`、`Editor/ServerClockWindow.cs`
*   `Tests/VolTech.Sandbox.Sample.Tests.asmdef`、`Tests/SampleFlowTests.cs`

asmdef は次のとおり。`overrideReferences: true`、`autoReferenced: false` は 3 つとも同じ。`precompiledReferences` は E2E の asmdef の一覧(Sample と Editor は `nunit.framework.dll` を除く)。

| asmdef | `includePlatforms` | `defineConstraints` | `references` |
|---|---|---|---|
| `VolTech.Sandbox.Sample` | 空 | `UNITY_EDITOR` | `UniTask`、`VolTech.Server.Abstractions`、`VolTech.MagicOnion.Client`、`UnityEngine.UI`、`Unity.InputSystem` |
| `VolTech.Sandbox.Sample.Editor` | `Editor` | なし | `UniTask`、`VolTech.Server.Abstractions`、`VolTech.MagicOnion.Client`、`VolTech.Sandbox.Sample` |
| `VolTech.Sandbox.Sample.Tests` | 空 | `UNITY_INCLUDE_TESTS` | `UnityEngine.TestRunner`、`UniTask`、`VolTech.Server.Abstractions`、`VolTech.MagicOnion.Client`、`VolTech.Sandbox.Sample` |

`includePlatforms` を `Editor` にすると、テストは EditMode に分類され、MonoBehaviour もシーンに付けられない恐れがあるので、Sample と Tests は空にする。

**Modify**

*   `VolTech-MagicOnion.slnx`: サンプルサーバーを足す
*   `README.md`: サンプルの動かし方。E2E の PlayMode の手順に `--filter VolTech.Sandbox.E2E` を足す
*   `docs/guide/index.md`: サンプルへの案内と、時刻の RPC を本番に写さない注意
*   `docs/designs/implementation/project-structure.md`: `samples/` と `Assets/Sample`
*   `docs/designs/index.md`: 承認済みに載せる
*   `docs/designs/sandbox-gacha-date-sample.md`: 実装で分かったことを戻す
*   `docs/index.md`: 手順 3 のチェックリスト。表の `Unity/VolTech.Sandbox` の行にサンプルのことを足す

### Verification Plan

サーバーの基盤の振る舞い(`DATE_CHANGED` の判定、再送)は `tests/VolTech.Server.Tests` が確かめ済みなので、dotnet のテストプロジェクトは作らない。

**PlayMode(`SampleFlowTests`、名前空間は `VolTech.Sandbox.Sample`)**: サンプルサーバーを起動してから、次を実行する。結果が 0 件でないことも確かめる。

```bash
unity test Unity/VolTech.Sandbox --mode PlayMode --filter VolTech.Sandbox.Sample
```

テストは `SampleSetup.Create` で組み立て、本物の `DefaultCommonErrorHandler` に、次の偽物を渡す。

*   `IErrorDialogView`: 呼ばれた回数を数え、`Ok` か `Title` をすぐ押す。「帯は出ない」は回数が 0 であることで確かめる
*   `IErrorNavigator`: `GoToTitleAsync` が呼ばれた回数を数える

`[UnitySetUp]` と `[UnityTearDown]` で `SampleClients.CreateClockClient` のクライアントから `SetOffsetAsync(0)` を呼ぶ。ジェムと無料の状態は `SampleSession.Player` を見る。

| テスト | 手順 | 確かめること |
|---|---|---|
| 日付が変わる流れ | 1. `EnterAsync` | 3000 ジェム、無料を引ける |
| | 2. 300 ジェムで引く | 2700 ジェム(`Common` の反映)、無料を引けるまま |
| | 3. 無料で引く | `Common` は null、2700 ジェムのまま、無料を引けない |
| | 4. もう一度無料で引く | `FREE_DRAW_USED`。帯は出ない |
| | 5. 300 ジェムで引く | 2400 ジェム、無料を引けないまま |
| | 6. `SetOffsetAsync(86400)` の後、300 ジェムで引く | `DATE_CHANGED`。`GoToTitleAsync` が 1 回呼ばれる。2400 ジェムのまま |
| | 7. `EnterAsync` | 2400 ジェム、無料を引ける |
| | 8. 無料で引く | 成功。2400 ジェムのまま、無料を引けない |
| ジェムが足りない | `EnterAsync` の後、300 ジェムで 10 回引き、もう 1 回引く | `NOT_ENOUGH_GEMS`。帯は出ない。`EnterAsync` で 0 ジェム |
| 時刻を戻す | 無料で引く → `SetOffsetAsync(86400)` → 300 ジェムで引いてタイトルへ → `EnterAsync` → 無料で引く → `SetOffsetAsync(0)` → 300 ジェムで引いてタイトルへ → `EnterAsync` | 無料を引ける(CUJ 3) |
| 時刻の RPC | トークン無しで `SetOffsetAsync(86400)` | 応答の `GameDate` が 1 日進む。`GetAsync` も同じ値 |
| | `SetOffsetAsync(-1)`、`SetOffsetAsync(3650 日 + 1 秒)` | `RpcException` の `StatusCode` が `InvalidArgument` |
| | `SetOffsetAsync(3650 日)` | 成功 |

**手で確かめる**: CUJ 1〜3 を Editor で操作する。ウィンドウと uGUI の画面はテストで触らないので、ここが唯一の確認になる。あわせて次も確かめる。

*   Play 中にウィンドウを開くと、時刻と日付が出る
*   サーバーを起動せずに Play モードにして「はじめる」を押すと、帯に「再試行」「タイトルへ」が出る。サーバーを起動して「再試行」を押すと進める
*   サーバーを起動していないとき、ウィンドウに失敗が表示される。サーバーを起動して「更新」を押すと値が出る
*   Play モードでないとき、ウィンドウのボタンが押せない

**既存のテスト**: dotnet と EditMode が通ること、E2E の PlayMode が `--filter VolTech.Sandbox.E2E` で 12 件通ることを確かめる。

### 実装で確かめた結果

2026-11-07 に、PlayMode の `SampleFlowTests` 4 件が通った。E2E の 12 件も `--filter VolTech.Sandbox.E2E` で通った。

使い捨てのテストで `Sample.unity` を読み込み、`Button.onClick` を呼んで、CUJ 1〜3 の画面の流れも確かめた。このテストはリポジトリに入れていない。本物のマウスの入力と、Server Clock ウィンドウはまだ確かめていない([チェックリスト](../index.md#確かめること))。

実装で変えたところは次のとおり。

*   `SampleUi` は `Camera` も作る。シーンは `SampleGame` の GameObject だけなので、カメラが無いと Game ビューに警告が出る
*   `IServerClockService` は `SampleClients` と同じアセンブリにあるので、`[MagicOnionClientGeneration]` には `ISampleGachaService` と `IDeviceService` だけを書く
