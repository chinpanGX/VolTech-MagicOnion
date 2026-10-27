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

UPM の `package.json` は NuGet のパッケージを依存に書けないので、Unity 側の上のライブラリはアプリが入れる。UPM の git URL は `Unity/VolTech.Sandbox/Packages/manifest.json`、NuGet のパッケージは `Unity/VolTech.Sandbox/Assets/packages.config` が見本になる。YetAnotherHttpHandler が要る System.IO.Pipelines と System.Runtime.CompilerServices.Unsafe は、Unity 7 が持っているので NuGet から入れない。

NuGetForUnity は依存を「求められる最低の版」で入れるので、依存も最新に上げて `packages.config` に固定する。サーバーも同じ理由で、`Grpc.AspNetCore.Server` 2.84.0 と `MessagePack` 3.1.12 を直接参照する(MagicOnion.Server 7.11.0 が引き込む MessagePack 3.1.7 には既知の脆弱性 GHSA-qhrr-8q5h-9q3h がある)。`Microsoft.NET.StringTools` は 18 系が .NET 11 向けなので、サーバー(net10)では 17 系の最新を使う。
