# サーバーでの使い方

## 登録

アプリのサーバーの `Program.cs` は次の形になる。

```csharp
var connectionString = builder.Configuration.GetConnectionString("Default")!;
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<IPlayerResolver, PlayerResolver>();   // アプリが実装する
builder.Services.AddVoltechServer(options => builder.Configuration.GetSection("Voltech").Bind(options));

var app = builder.Build();
VoltechDatabase.Migrate(connectionString);                         // 基盤のテーブル。アプリのマイグレーションより先に
app.MapMagicOnionService();
```

- `NpgsqlDataSource` は 1 つだけ登録する
- `IPlayerResolver` は、player_id からプレイヤーの状態(NotCreated / Active)を返す。アプリが実装する
- サーバーは HTTP/2 で待ち受ける。TLS なし(h2c)で動かすときは、Kestrel の `Protocols` を `Http2` にする
- `VoltechDatabase.Migrate()` は基盤のテーブルだけを作る。適用済みの記録は `_voltech_migrations` に入るので、アプリのマイグレーションの記録とは混ざらない

## 設定

`appsettings.json` の `Voltech` セクションに書く。書き間違いは起動時に検証して止まる。

```json
{
  "Voltech": {
    "GameDay": { "ResetHour": 4, "TimeZone": "Asia/Tokyo" },
    "AccessTokenLifetime": "01:00:00"
  }
}
```

| 項目 | 既定 | 意味 |
| --- | --- | --- |
| `GameDay.ResetHour` | 0 | ゲーム内の日付が変わる時(0〜23) |
| `GameDay.TimeZone` | `UTC` | 日付を数えるタイムゾーン(IANA の名前) |
| `AccessTokenLifetime` | 1 時間 | 発行するトークンの有効期限 |

`GameDay` はファイルを書き換えると読み直す。

## ハンドラー

書き込みを `ITransactions.RunAsync` で包む。冪等性(同じ `x-request-id` の再送で二重に実行しない)は基盤が受け持つ。

```csharp
public async UnaryResult<DrawResult> DrawAsync(int count)
{
    return await transactions.RunAsync(Context, async tx =>
    {
        var today = Context.GetGameDate();
        var player = Context.GetAuthContext().PlayerId;
        // tx.CreateCommand(...) で読み書きし、所持データの変化は tx.WriteGameLog(GameLog.Quantity(...)) で残す
        ...
    });
}
```

- 業務エラーは `BusinessException(ErrorCode, message, metadata?)` を投げる。ステータスへの変換は基盤が行う
- 読み取りだけの RPC は、共有インターフェースのメソッドに `[NoSideEffects]` を付ける。読み取り専用のトランザクションで動き、冪等性の対象から外れる
- 認証の要らない RPC には `[SkipAuth]` を付ける。共有インターフェースに付けると、そのサービスのすべての RPC が対象になる。メソッドに付けると、その RPC だけが対象になる。`[SkipAuth]` の RPC は冪等性の対象にもならないので、副作用のある `[SkipAuth]` の RPC(引き継ぎなど)は、何度呼んでも結果が同じになるように作る(通信のエラーで送り直されても、基盤は二重の実行を防がない)
- RPC は共有インターフェースに直接書く。基底インターフェースから受け継いだメソッドには対応しない(属性が読まれず、クライアントのラッパーからも呼べない)
- 属性は共有インターフェースに付ける。実装クラスのメソッドに付けてもコンパイルは通るが、何も起きない(エラーにもならない。効くのは共有インターフェースに付けた属性だけ)。クラスに付けるとコンパイルエラーになる
- Dapper を使うときは、`tx.Connection` と `tx.Transaction` を渡す
- ゲームログは、数量の変化を `GameLog.Quantity(kind, delta, after, reason)`、それ以外を `GameLog.Event(kind, reason)` で作り、`Target(id)`・`Field(name, value)` で足す

共有インターフェースは、`IService<T>` を継承し、Unity と共有するインターフェース。サーバーのサービスのクラスは、`ServiceBase<T>` の `T` にこれを渡す。クライアントのラッパー(`ApiServiceBase<TService>`)にも同じものを渡す。

```csharp
// 共有コード(サーバーと Unity の両方で読み込む)
public interface IGachaService : IService<IGachaService>
{
    [NoSideEffects]
    UnaryResult<GachaInfo> GetInfoAsync();

    UnaryResult<DrawResult> DrawAsync(int count);
}

// サーバー
public sealed class GachaService : ServiceBase<IGachaService>, IGachaService
{
    ...
}
```

## 共通レスポンス項目

player_diff・バッジ・通知は、アプリが 1 つの型(下の `AppCommon`)にまとめ、レスポンスの型に `IApiResponse<AppCommon>` を実装する。基盤は中身を知らない。

```csharp
// 共有コード
[MessagePackObject]
public sealed class AppCommon
{
    [Key(0)] public PlayerDiff? PlayerDiff { get; set; }          // 変更後の値。無くなったものは ID の一覧で持つ
    [Key(1)] public Badge[]? Badges { get; set; }                 // 含まれない種類は変化なし
    [Key(2)] public Notification[]? Notifications { get; set; }
}

[MessagePackObject]
public sealed class DrawResult : IApiResponse<AppCommon>
{
    [Key(0)] public AppCommon? Common { get; set; }               // null なら変化なし。基盤が入れる
    [Key(1)] public int[] CardIds { get; set; } = Array.Empty<int>();
}

// サーバー: コレクター。トランザクションごとに 1 つ作られる。コンストラクターで DI の部品を受け取れる
public sealed class AppCollector : ICommonResponseCollector<AppCommon>
{
    public void UpsertCard(Card card) { ... }                     // 書き込み関数が記録する
    public ValueTask<AppCommon?> BuildAsync(TxContext tx) { ... } // 本体の成功後、同じトランザクションの中で呼ばれる
}

builder.Services.AddVoltechServer(...).AddCommonResponse<AppCommon, AppCollector>();

// 書き込み関数
tx.GetCollector<AppCollector>().UpsertCard(card);
```

- ハンドラーは `Common` を自分で入れない。`RunAsync` が本体の後に `BuildAsync` を呼んで入れ、冪等性のためにレスポンスごと保存する。同じ `x-request-id` の再送には同じ `Common` が返る
- 本体が失敗したら、コレクターの中身は捨てられる
- `IApiResponse<>` を実装したレスポンスなのに `AddCommonResponse` を呼んでいない、または `TCommon` が違うと `INTERNAL` になる
- 使わないレスポンスには `IApiResponse<>` を実装しない(実装すると、クライアントの反映処理が要る)

振る舞いの詳細は [仕様書](../spec/index.md)。
