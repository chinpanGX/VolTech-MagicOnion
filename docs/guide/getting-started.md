# 導入

## アプリのリポジトリへの置き方

アプリのリポジトリは `client/`(Unity)・`server/`・`shared/` に分かれている前提で、VolTech-MagicOnion を `external/` の下にサブモジュールとして置く。MasterDataTools など、ほかのサブモジュールも `external/` に集める。

```
MyGame/
├─ client/
│   └─ Packages/manifest.json    "file:../../external/VolTech-MagicOnion/Unity/com.chinpangx.voltech.magiconion"
├─ server/
│   └─ MyGame.Server.csproj      ../external/VolTech-MagicOnion/src/VolTech.Server を参照
├─ shared/                        アプリの IService 定義・レスポンス型(UPM パッケージ + csproj)
└─ external/
    ├─ VolTech-MagicOnion/       サブモジュール
    └─ MasterDataTools/
```

参照の向き:

```
shared → VolTech.Server.Abstractions
server → shared, VolTech.Server
client → shared, VolTech の Unity パッケージ
```

- 基盤は NuGet では配らない。サーバーはプロジェクト参照、Unity は UPM のローカルパッケージ(`file:`)で使う
- `shared/` は、`.cs` の実体を UPM パッケージに置き、サーバー側は csproj から `Compile Include` で読み込む
- 使う版は、サブモジュールでタグ(`v0.1.0` など)を checkout して固定する

この形にした理由は [実装設計書のプロジェクト構成](../design/project-structure.md#アプリからの参照)。

## 使う版

アプリのサーバーとクライアントも、基盤と同じ版を使う。版が違うと、ビルドで警告かエラーになる。

| ライブラリ | 版 | Unity での入れ方 |
|---|---|---|
| Unity | 7000.0.0a12 | |
| MagicOnion(.Client / .Server) | 7.11.0 | NuGetForUnity で `MagicOnion.Client`、UPM で `MagicOnion.Client.Unity` |
| MessagePack | 3.1.12 | NuGetForUnity で `MessagePack`、UPM で `MessagePack.Unity` |
| YetAnotherHttpHandler | 1.11.7 | UPM |
| Grpc.Net.Client / Grpc.Core.Api / Grpc.Net.Common | 2.84.0 | NuGetForUnity(MagicOnion.Client の依存。最低の版で入るので、最新を明示して入れる) |
| NuGetForUnity | 4.5.0 | UPM |
| UniTask | 2.5.11 | UPM(クライアントの `ApiCore` が使う) |
| Google.Protobuf | 3.36.2 | NuGetForUnity(エラー詳細の解析とアカウント保存) |
| Npgsql | 10.0.3 | (サーバーだけ) |
| dbup-postgresql | 7.0.1 | (サーバーだけ) |
| Ulid | 1.4.1 | (サーバーだけ) |
| Grpc.StatusProto | 2.84.0 | (サーバーだけ) |

UPM の `package.json` は NuGet のパッケージを依存に書けないので、Unity 側の上のライブラリはアプリが入れる。UPM の git URL は `Unity/VolTech.Sandbox/Packages/manifest.json`、NuGet のパッケージは `Unity/VolTech.Sandbox/Assets/packages.config` が見本になる。YetAnotherHttpHandler が要る System.IO.Pipelines と System.Runtime.CompilerServices.Unsafe は、Unity 7 が持っているので NuGet から入れない。

### Microsoft.Bcl.AsyncInterfaces を手で置く

IL2CPP でプレイヤーをビルドすると、UnityLinker が `IL1009: Assembly reference 'Microsoft.Bcl.AsyncInterfaces' could not be resolved` で止まる(Unity 7000.0.0a12)。

- `MagicOnion.Client` が依存する `Microsoft.Bcl.TimeProvider` には netstandard2.0 版しかなく、`IAsyncDisposable` を `Microsoft.Bcl.AsyncInterfaces` から参照する
- Unity 7 は `Microsoft.Bcl.AsyncInterfaces` を `Editor/Data/BCLExtensions/runtime/netstandard2.1/` に持っているので、NuGetForUnity は入れさせない(Install が押せない)。Editor ではこれが読み込まれるので動く
- プレイヤーのビルドでは、リンカーは `BCLExtensions/runtime/net10.0/` だけを探す。そこにはこの DLL が無い

nuget.org の `Microsoft.Bcl.AsyncInterfaces` 10.0.13 から `lib/netstandard2.1/Microsoft.Bcl.AsyncInterfaces.dll` を取り出し、`Assets/Plugins/Microsoft.Bcl.AsyncInterfaces/` に置く。Inspector で Editor を外し、プレイヤーのビルドにだけ含める(Editor の持つ DLL と重ねないため)。見本は Sandbox の同じ場所にある。Unity が net10.0 側にこの DLL を足したら要らなくなる。

### 版の固定

NuGetForUnity は依存を「求められる最低の版」で入れるので、依存も最新に上げて `packages.config` に固定する。基盤のサーバーは `Directory.Packages.props` で版を一か所に固定し、推移的な依存もその版にそろえる(MagicOnion 7.11.0 が引き込む MessagePack 3.1.7 には既知の脆弱性 GHSA-qhrr-8q5h-9q3h があるので、3.1.12 に上げている)。アプリのサーバーも、同じ版を固定する。`Microsoft.NET.StringTools` は 18 系が .NET 11 向けなので、サーバー(net10)では 17 系の最新を使う。
