# VolTech-MagicOnion を MagicOnion のベストプラクティスに沿って見直す

* **Author:** ちんぱん
* **Date:** 2026-11-04

## Overview

VolTech-MagicOnion は、C# のサーバーと Unity のクライアントを MagicOnion 7.11.0(最新)で作る基盤で、個人開発のゲーム Uta が使っている。VolTech-gRPC(proto + Rust)と同じ機能を持つ基盤と位置づけ、そこから次の 4 つを除く: 強制アップデートの判定、メンテナンスの判定、アカウント停止、シャーディング。

ベストプラクティスに合わせて作り直すことは目的にしない。コードの書き方や構造の作りで迷ったときに、ベストプラクティスに沿う方を選ぶ(判断に迷ったときの決め手として使う)。何がベストプラクティスかは、次の順に調べて決める。

1. MagicOnion 7.11.0 の公式ドキュメント
2. MagicOnion のリポジトリのソースとサンプル
3. 1 と 2 に記述が無いところは、.NET と ASP.NET Core の標準の API と推奨

gRPC 版と命名・使い心地をそろえるのは、ベストプラクティスとぶつからない範囲だけにする。内部の作りは、MagicOnion に合う形に変えてよい。

見直す範囲は 3 つ。

- **作り終えた部分**: `ApiCore`、フィルター、`RpcSettings`、認証、トランザクション
- **未マージの共通レスポンス項目**: `feature/common-response` ブランチ
- **gRPC 版との残りの差分**: ログ、計測、ゲームログ、Monitor、`Runtime/Unity/` の部品、テスト用の偽物、`StoredAccount` の保存形式

この文書では全体の方針と進める順番を決める。設計書は項目ごとに分けて `docs/designs/` に書く。

## Analysis

背景を持たないレビュー担当 3 人(zoom-out・design・codebase-details)が仮説を見た。重なった指摘はまとめてある。

### 1. 「ベストプラクティス」だけでは、作り直す理由にならない

公式ドキュメント(基準 1)が監視について書いているのは、StreamingHub のメトリクス(Meter `MagicOnion.Server`)だけ。基盤は Unary しか使わず、StreamingHub はスコープ外なので、この記述は当てはまらない。Unary のフィルターの組み方、クライアントのラッパー、ログの形式、テスト用の偽物についても、公式はほとんど何も書いていない。そのため実際の判断は、ほとんどが基準 3 か、作る人の好みに落ちる。

コードを見た担当の結論は次のとおり。

- **サーバーのフィルターと `RpcSettings`**: すでに MagicOnion の素直な使い方になっている(`GlobalFilters` に Order 付きで登録し、共有インターフェースの属性をインターフェースマップで読む)。
- **テスト用の偽物**: すでに共有インターフェース(`IService<T>`)を実装した偽のクライアントで書かれている。`FakeCallInvoker` を置き換える仕事は、もう残っていない。
- **基準 1 から外れている大きな点はクライアントのラッパーだけ**: `ApiCore` は、呼ぶたびに `WithOptions(callOptions)` でヘッダーと締め切りを付けていて、MagicOnion の `IClientFilter` を使っていない。

すでに動いていてテストも通る部分(サーバー 43 件、EditMode 50 件)を「ベストプラクティスだから」という理由だけで作り直すと、作業が増えるだけで得るものが無い。

### 2. クライアントとの取り決めが、ベストプラクティスより下に来ている

`docs/spec/index.md` は「Unity クライアントとの取り決め(エラー形式・ヘッダー)は変えない」を前提にしている。ところが仮説では、ベストプラクティスが最上位にある。このままだと、ErrorInfo のトレーラーを MagicOnion 流の例外に置き換える、といった変更が規則の上では正しくなってしまう。

### 3. まだ確かめていない Unity の項目が、設計を先に決めてしまう

`docs/index.md` には、確かめていない項目が残っている。

- YetAnotherHttpHandler で `grpc-status-details-bin` がクライアントまで届くか
- Windows の IL2CPP のプレイヤーからサーバーを呼べるか
- iOS の AOT で MagicOnion のクライアントと MessagePack が動くか
- `Abstractions` の型の MessagePack のフォーマッターが生成されるか
- `[SkipAuth]`・`[NoSideEffects]` の属性を IL2CPP でリフレクションで読めるか

どれかが失敗すると、クライアントの作りが変わる。たとえば属性が読めなければ、属性を読む部分をソースジェネレーターに変えることになる。これを確かめる前に設計し直すと、二度手間になりうる。

### 4. ログと計測は、基準 3 で決まる。基盤で作る部品は少ない

- **運用ログ**: `RpcLogFilter` は、すでに `ILogger` のメッセージテンプレートとスコープで、RPC ごとに 1 行のアクセスログを出している。足りないのは、JSON で出す設定(`AddJsonConsole` か ZLogger)、Cloud Logging の `severity` への対応付け、トレース ID との相関の 3 つ。新しく作るものではない。
- **サーバーの計測**: ASP.NET Core・Grpc.AspNetCore・Npgsql が出す `Activity` とメトリクスを、OpenTelemetry .NET の標準の計装で拾えばよい。Cloud Run のトレースヘッダーも引き継ぐ。終了した `MagicOnion.Server.OpenTelemetry`(5.1.8-preview で止まっている)の代わりを自分で作るのは避ける。
- **基盤が持つもの**: スコープに載せる項目名の取り決め(player_id、リクエスト ID など)と、導入の手順だけ。
- **コスト**: 1 RPC につきアクセスログ 1 行にゲームログが加わるので、Cloud Logging の取り込み量を見積もり、ログを出す粒度の規則を決める必要がある。

### 5. 共通レスポンス項目は、トランザクションと冪等性に深く結びついている

未マージの実装は、`ITransactions.RunAsync` の中で、保存とコミットの前に `Common` を詰めている。MagicOnion の流儀(サービスフィルター)とは違うが、理由はある。同じトランザクションの中で詰め、冪等性のためにレスポンスを保存するので、再送されたときも同じ値を返せる。設計書にはこの理由を残す。

指摘された問題は次のとおり。

- **`Common` が黙って空になる**: `RunAsync` を通らない RPC では、`IApiResponse<>` を実装していても `Common` が入らず、エラーにもならない。
- **保存済みのレスポンスとの互換**: 保存した MessagePack のバイト列には、変更前の `TCommon` の形が残っている。`TCommon` の型を変えたあとの再送で、新しい型として読めないおそれがある。
- **IL2CPP(AOT)への依存**: ジェネリックの `IApiResponse<TCommon>` のフォーマッター生成と、実行時の型の判定が AOT に依存する。
- **部品が多い**: 1 つのアプリにとっては部品が多い。クライアントの反映処理を、`TCommon` を受け取る 1 つの関数にまとめる案が出た。そうすれば、型の食い違いを調べる処理も要らなくなる。
- **一緒に設計しないと二度直す**: Transaction と `ApiCore` を見直すなら、共通レスポンス項目も同じ単位で設計しないと、二度直すことになる。

### 6. ゲームログを標準出力に出すには、出力先を選べるようにする必要がある

今は `CommitAsync` の中で `game_logs` テーブルに INSERT するだけになっている。gRPC 版は、出力先として Database と Stdout を選べる(環境変数 `GAME_LOG_SINK`。Stdout はコミットが成功してから JSON で出す)。さらに、`RunAsync` の外(内部 API や定期実行のジョブ)からもゲームログを書ける。

MagicOnion 版で同じことをするには、`ITransactions` の外からゲームログを書く API の追加まで必要になる。また、DB と標準出力のどちらを正にするかを決めないと、両方に書いた内容が食い違う。

### 7. Monitor・Tracing・クライアントの計測は、1 つの判断にまとまる

`IClientFilter` か `CallInvoker` を使えば、シリアライズ前のリクエストとレスポンスを見られる(フィルターはレスポンスの型を知らないので、[設計書 1](common-response-and-client-filter.md) では `CallInvoker` で読む)。そうなると、「Monitor は protobuf のメッセージを記録する前提」という保留の理由はなくなる。ただし画面に表示するには、MessagePack の値を JSON に変える処理と、リリースのビルドから外す仕組みが要る。開発中だけ使うツールなので、Uta の開発で困るまで後回しにできる。`Runtime/Unity/` の部品も同じ扱いで、Uta が使うものだけを移せばよい。

### 8. `StoredAccount` の保存形式は、端末に保存済みのデータがあるかで決まる

今は Google.Protobuf の生成コードで保存している。MessagePack に移すと、端末に保存済みのアカウントファイルを読めなくなる。Uta をリリースした後に移すなら、古い形式を読む経路を必ず残す必要がある。リリース前なら、移すのは今が一番安い。設計書を 1 本立てるほどの話ではないので、この文書で決めれば足りる。

### 9. Uta への破壊的な変更の扱い

`ApiServiceBase<TService>`、`RpcSettings`、`AddVoltechServer()`、`TxContext` の名前や形を変えると、Uta 側も同時に直すことになる。0.x の間なら破壊的に変えてよいのかを、先に決めておく必要がある。

### 10. そのほか

- **フィルターの手前での失敗**: リクエストのデシリアライズの失敗など、フィルターより前で起きた失敗は、アクセスログに出ない。
- **Cloud SQL の接続数**: Cloud Run と Cloud SQL では、Npgsql の接続プールの上限にインスタンスの最大数を掛けた値が、Cloud SQL の接続上限を超えないようにする。これを基盤の推奨設定として書いておく。
- **置き換えた節の明記**: 項目ごとの設計書には、置き換える `docs/spec/`・`docs/designs/implementation/` の節を書く。

## Goals vs. Non-Goals

* **In-Scope Goals:**
  * 作り終えた部分は、実害(バグ、AOT で動かない、保守の手間)があるところと、下の項目で触るところだけを直す。ベストプラクティスは、そのとき書き方や構造で迷った場合の決め手にする
  * クライアントとの取り決め(ステータスと ErrorInfo、ヘッダー名、予約エラー、冪等性の意味)を「変えないもの」として、ベストプラクティスより上に置く
  * 確かめていない Unity の項目(IL2CPP・AOT・トレーラー)を、設計し直す前に確かめる。ただし設計書 1 では、IL2CPP の確認を前提にしないと決めた(2026-11-07。設計書 1 の Q8)。`grpc-status-details-bin` が届くことは設計書 1 の実装の中で確かめた
  * クライアントの共通の処理を `IClientFilter` に移す(基準 1 から外れている唯一の大きな点)。ヘッダーは、`Metadata` の使い回しのため、基盤が包む `CallInvoker` で付ける([設計書 1](common-response-and-client-filter.md))
  * 共通レスポンス項目を、Transaction と `ApiCore` と同じ単位で見直す
  * サーバーとクライアントのアプリの書き方を MagicOnion の公式に合わせる。クライアントは共有インターフェースを直接呼ぶ(2026-11-07 に追加。[設計書 1](common-response-and-client-filter.md))
  * 運用ログを JSON で出し、Cloud Logging とトレースに相関させる。サーバーの計測は OpenTelemetry .NET の標準の計装で拾う
  * ゲームログの出力先を選べるようにし、`RunAsync` の外から書く手段を足す
  * Monitor を作る: 全通信(トークンの発行を含む)を記録し(記録の場所は設計書 4 で決める。`IDeviceService` は基盤のフィルターを通らない)、Editor のウィンドウと実機の通信の表示を gRPC 版と同じ使い心地にする。Body は MessagePack を JSON に変えて出す(grpcurl に貼れる利点は無くなる)
* **Out-of-Scope Non-Goals:**
  * 強制アップデートの判定、メンテナンスの判定、アカウント停止、シャーディング
  * StreamingHub、マスターデータの配信、MasterDataTools(`docs/spec/index.md` のスコープ外のまま)
  * サーバーの状態の設定ファイル(`server_status.toml`)の読み直し。主な用途がメンテナンスと強制アップデートで、`GameDay` は `IOptionsMonitor` で足りている
  * MagicOnion 用の OpenTelemetry の連携を自分で作ること
  * `FakeCallInvoker` の置き換え(すでに共有インターフェースの偽物で済んでいる)。設計書 1 では基盤のテストの中だけで、本物のクライアントを通すための `FakeCallInvoker` を作る
  * 反映処理の登録忘れを起動時に調べる仕組み(gRPC 版にも無い)
  * 当面の対象外: `Runtime/Unity/` の部品のうち Uta が使わないもの

## Key Decisions & Staging for Next Steps

### 決めたこと

* 位置づけ: gRPC 版から 4 つを除いた機能を持つ基盤。そろえるのは命名と使い心地で、内部の作りは MagicOnion に合わせて変えてよい
* 判断の規則: 作り直しは必須にしない。書き方や構造で迷ったときに、MagicOnion のベストプラクティスに沿う方を選ぶ。gRPC 版にそろえるのは、ぶつからない範囲だけ(2026-11-04、Analysis 1 の指摘を受けて)
* Windows の IL2CPP のプレイヤーからサーバーを呼べることは、ユーザーが確かめ済み(2026-11-04 時点。日付と方法は docs/index.md に書く)
* ベストプラクティスの基準: MagicOnion 7.11.0 の公式ドキュメント → リポジトリのソースとサンプル → .NET と ASP.NET Core の標準
* 見直しの範囲: 作り終えた部分、未マージの共通レスポンス項目、残りの差分の 3 つ
* 設計書は項目ごとに分けて `docs/designs/` に書く

### まだ決めていないこと(ユーザーが決める)

1. (解決)ログと Monitor(Unity の Editor で通信を 1 件ずつ見るウィンドウ)は作る(2026-11-04)。`docs/spec/index.md` の「運用のための機能は落とす」は、これらを含める形に書き換える
2. (解決)クライアントとの取り決め(ステータスと ErrorInfo、ヘッダー名、予約エラー、冪等性の意味)は変えない。ベストプラクティスより上に置く(2026-11-04)
3. (解決)Uta はリリース前。基盤への破壊的な変更は許し、Uta の開発と合わせて基盤も直していく(2026-11-04)。端末に保存済みのデータが無いので、移行を考えずに形式を変えられる
5. (解決)Unity クライアントの基盤のコードは、できるだけ Google.Protobuf を直接使わない作りにする(2026-11-04)。他のパッケージが間接的に Google.Protobuf を要求するのは問題にしない。
   * `StoredAccount`(端末に暗号化して保存する DeviceId・SecretKey・PlayerId)は MessagePack に移す。Supplement は参照しない(形式の考え方だけ合わせる)
   * `grpc-status-details-bin` の読み取り(`RpcErrorDetails`)は、Google.Protobuf を使う方が楽ならそのままでよく、よりよい方法があれば乗り換えてよい。設計書で比べて決める。今は手書きで、Google.Protobuf から使っているのは `CodedInputStream` だけ。手書きにした理由は、Unity の Editor が同梱する Google.Protobuf と版がずれると ErrorInfo を読めなくなるため(`RpcErrorDetails.cs` のコメント)。取り決め(`google.rpc.Status` と `ErrorInfo` のバイト列)は変えない
   * サーバーは Grpc.AspNetCore が Google.Protobuf に依存するので、そのままにする(`ErrorStatus` が `Google.Protobuf.WellKnownTypes` を使っている)
4. (解決)作り直しの入口の条件 → 作り直しは必須にせず、迷ったときの決め手にすると決めた

### 進める順番(案)

1. 確かめていない Unity の項目を確かめる。最優先は YetAnotherHttpHandler で `grpc-status-details-bin` が届くか(取り決めを変えないので前提になる)。残りは iOS の AOT、フォーマッターの生成、属性の読み取り(Windows の IL2CPP で呼べることは確かめ済み)
2. 変えないもの(クライアントとの取り決め)を `docs/spec/` で固める
3. 設計書 1: `ApiCore` の処理を `IClientFilter` に移し、アプリの書き方を公式に合わせる。共通レスポンス項目と Transaction・`ApiCore` を一緒に見直す
4. 設計書 2: ログと計測(JSON の出力、相関、項目名の取り決め、OpenTelemetry の標準の計装、Cloud Logging の量の見積もり)
5. 設計書 3: ゲームログの出力先の選択と、`RunAsync` の外から書く API
6. 設計書 5(小さい): `StoredAccount` を MessagePack に移し、`RpcErrorDetails` の読み取り方を決める(Google.Protobuf のままか、自前か)。Monitor の `ErrorInfo` の表示も同じ読み取りを使うので、設計書 4 より先に置く
7. 設計書 4: Monitor(通信の記録、記録の置き場所、Editor のウィンドウ、実機からの送信)
8. 後回し: `Runtime/Unity/` の部品、テスト用の偽物をアプリに配る形
