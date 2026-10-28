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
- 認証の要らない RPC は `[SkipAuth]` を付ける
- Dapper を使うときは、`tx.Connection` と `tx.Transaction` を渡す
- ゲームログは、数量の変化を `GameLog.Quantity(kind, delta, after, reason)`、それ以外を `GameLog.Event(kind, reason)` で作り、`Target(id)`・`Field(name, value)` で足す

振る舞いの詳細は [仕様書](../spec/index.md)。
