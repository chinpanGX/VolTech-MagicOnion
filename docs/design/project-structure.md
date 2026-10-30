# プロジェクト構成

プロジェクトは、Unity と共有するかどうかの 1 本の境界で 2 つに分ける。サーバー側の関心ごと(認証・日付・トランザクションなど)は、プロジェクトではなくフォルダーと名前空間で分ける。

```
VolTech-MagicOnion/
├─ src/
│   ├─ VolTech.Server.Abstractions/    csproj だけ。ソースは Unity パッケージ側を読み込む
│   └─ VolTech.Server/                 サーバー側の全部
├─ tests/
│   ├─ VolTech.Server.Abstractions.Tests/
│   └─ VolTech.Server.Tests/           単体テストと結合テスト(Testcontainers)
└─ Unity/
    ├─ com.chinpangx.voltech.magiconion/
    │   ├─ Runtime/Abstractions/       共有ソースの実体 + asmdef
    │   ├─ Runtime/Client/             ApiCore など + asmdef
    │   └─ Tests/EditMode/             Runtime/Client のテスト + asmdef
    └─ VolTech.Sandbox/                開発用の Unity 7 プロジェクト。パッケージを file: で参照する
```

`VolTech.Sandbox` は、パッケージの `.meta` を作る、Unity でコンパイルが通るか確かめる、テストを動かす、IL2CPP でビルドして確かめる、のに使う。基盤の確認がアプリなしで済む。`Unity/Directory.Build.props` は、リポジトリ直下の `Directory.Build.props`(Nullable など)を Unity が作る csproj に効かせないためのもの。NuGet の版は、リポジトリ直下の `Directory.Packages.props` で一か所に固定する(推移的な依存も含む)。

| プロジェクト | 中身 | Unity と共有 |
| --- | --- | --- |
| `VolTech.Server.Abstractions` | `ErrorCode`、予約エラーの定数(`ReservedErrors`)、`BusinessException`、ヘッダー名の定数、属性(`[SkipAuth]`・`[NoSideEffects]`)、`IDeviceService` と結果の型、`IApiResponse`(未実装) | する(netstandard2.1) |
| `VolTech.Server` | 下の表のフォルダーすべて | しない(net10) |

| `VolTech.Server` のフォルダー | 中身 |
| --- | --- |
| `Status/` | サーバー時刻とゲーム内の日付、日付変更の判定、`ServerTimeFilter`・`DateChangeFilter` |
| `Auth/` | `DeviceService`、トークン、`AuthFilter`、`IPlayerResolver` |
| `Transaction/` | `ITransactions`・`TxContext`、冪等性、ゲームログ、基盤のテーブルのマイグレーション(`VoltechDatabase`) |
| `Logging/` | `RpcLogFilter`(アクセスログと例外の変換)。ログの出力先はアプリが決める(ZLogger を推奨) |
| `Hosting/` | `AddVoltechServer()`、設定(`VoltechServerOptions`)、`ServiceContext` の拡張メソッド。フィルターの順序をここで固定する |
| `Internal/` | フィルターどうしで共有する内部の部品(RPC ごとの設定表、エラーの組み立て) |

## 2 つに分ける理由

最初は Rust 版の 5 クレートと 1 対 1 に分ける案だった。別の製品として作り直すことにしたので、その理由はなくなった。中身を見ると、細かく分けても得るものがない。

- 依存が切れない。`Auth` は端末の登録で DB を使うので `Transaction` に依存し、`Hosting` はすべてに依存する。アプリは `Hosting` を参照するので、結局すべてが入る
- `internal` が使えない。プロジェクトをまたいで型を渡すため、フィルター本体や RPC ごとの設定表まで `public` にするか、`InternalsVisibleTo` を並べることになる。1 つにまとめれば、公開するのは `AddVoltechServer()`・`ITransactions`・`IPlayerResolver`・`AuthContext` の取り出し口などに絞れる
- 本当に必要な境界は「Unity と共有するか」だけ。`Abstractions` は netstandard2.1 で、サーバー専用の依存(Npgsql など)を持ち込めないので、ここだけは別のプロジェクトにする

関心ごとの分離は、`internal` で公開範囲を絞ることと、依存の向きを確かめるテストで守る。テストは NetArchTest(NetArchTest.Rules 1.3.2)で書き、ルールをコードとして残す(`tests/VolTech.Server.Tests/ArchitectureTests.cs`)。

| ルール | 理由 |
| --- | --- |
| DB(Npgsql・DbUp)を参照するのは `Auth/` と `Transaction/` だけ。`VolTech.Server` のほかの型(今は `Status/`・`Logging/`・`Hosting/`・`Internal/`。フォルダーを足しても対象に入る)は参照しない | DB を使うのは端末の登録とトークン(`Auth/`)と、トランザクション・冪等性・ゲームログ・マイグレーション(`Transaction/`)。サーバー時刻と日付変更の判定は DB を使わない([リクエストの処理順](../spec/processing-order.md)) |
| `Internal/` は、ほかのフォルダー(`Auth/`・`Status/`・`Transaction/`・`Logging/`・`Hosting/`)に依存しない | フィルターどうしで共有する部品なので、使う側より下に置く |
| `Abstractions` は、`VolTech.Server` のコードと、サーバー専用の依存(Npgsql・DbUp・MagicOnion.Server・ASP.NET Core)を参照しない | Unity と共有する(netstandard2.1) |

DB のルールのテストは、名前空間を並べず「`Auth/`・`Transaction/` 以外のすべての型」を対象にする。フォルダーを足したときに、テストに足し忘れても対象から漏れない。

除外は `DoNotResideInNamespaceMatching` と正規表現 `^VolTech\.Server\.(Auth|Transaction)(\.|$)` で書く。`DoNotResideInNamespace` は前方一致なので、`VolTech.Server.Authorization` のような名前空間まで除外してしまう。`Auth/`・`Transaction/` の下のフォルダー(`VolTech.Server.Auth.Tokens` など)も DB を参照してよい(テストの対象から外す)。ルールが効くことは、Npgsql を参照する型を `Status/` と `VolTech.Server.Authorization`(前方一致なら除外されてしまう名前空間)に一時的に足し、どちらでもテストが失敗するのを見て確かめた(2026-10-30。正規表現に変えた後)。

`Hosting/` の `ServiceContextExtensions` は名前空間が `VolTech.Server`(ルート)なので、テストでは `Hosting/` の型として扱う。今のコードでは、`Auth/` は `Transaction/` を経由せず Npgsql を直接使い(`DeviceStore`)、`Auth/`・`Status/` は設定(`Hosting/` の `VoltechServerOptions`)を参照する。

## 共有のしかた

- Unity から参照するのは `Abstractions` と `Runtime/Client` だけにする。サーバーのコードは Unity パッケージの外に置くので、Unity に入らない
- `Abstractions` はソースで共有する。`.cs` の実体は UPM パッケージの中(asmdef 付き)に置き、サーバーの csproj はそのフォルダーを `Compile Include` で読み込む。UPM パッケージはパッケージの外のファイルを含められないので、実体はパッケージ側に置く。DLL を配る形と違い、サーバーと Unity のどちらからでも定義へ飛べる
- `Abstractions` は MagicOnion.Abstractions と MessagePack.Annotations を参照する(`IDeviceService` と結果の型をクライアントと共有するため)。Unity では NuGetForUnity で入れた DLL を asmdef が自動で参照する
- Unity 7 のアナライザーは、static フィールドを持つ型に Unity の属性(`AutoStaticsCleanup` など)を求める(UAL0010)。`Abstractions` は Unity を参照できないので、static フィールドは定数と、変えられない値(`ReservedErrors` の `ErrorCode`)だけにする
- C# 9 の Unity は nullable をファイルごとに有効にする必要がある。`?` を使うファイルの先頭に `#nullable enable` を書く
- 結合テストが遅くなり CI で分けたくなったら、そのときにテストプロジェクトを分ける

## アプリからの参照

アプリのリポジトリには、基盤をサブモジュールとして置く。フォルダーの置き方と手順は [利用ガイドの導入](../guide/getting-started.md)。ここには、その形にした理由を書く。

- NuGet で配らず、プロジェクト参照と UPM のローカルパッケージで使う。ソースが手元にあるので、サーバーからも Unity からも基盤の定義へ飛べる
- アプリの `shared/` も `Abstractions` と同じ仕組みにする。`.cs` の実体は UPM パッケージに置き、サーバー側は csproj から読み込む
- サブモジュールの置き場所を `Packages/` にしないのは、Unity の `client/Packages/` と紛らわしいため
- アプリのサーバーは、基盤と同じ版の NuGet パッケージ(MagicOnion など)を使う。版が違うとビルドで警告かエラーになるので、基盤が使う版を利用ガイドに書く
- UPM の `package.json` は NuGet のパッケージを依存に書けない。Unity 側の MagicOnion.Client・MessagePack などは、アプリが NuGetForUnity などで入れる。入れるものは利用ガイドに書く
