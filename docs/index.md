# docs

設計と利用ガイドの置き場所。設計・実装・ドキュメントは、このリポジトリの中で行き来しながら更新する。

## 計画しているプロジェクト

Rust 版(VolTech-gRPC)の `auth`・`error`・`log`・`status`・`transaction` クレートと同じ関心の分け方にする。

| プロジェクト | 中身 | 状態 |
|---|---|---|
| `VolTech.Server.Abstractions` | `ErrorCode`、`BusinessException`、ヘッダー名、属性。Unity と共有(netstandard2.1) | あり |
| `VolTech.Server.Status` | メンテナンス・強制アップデート・日付変更の判定 | 未着手 |
| `VolTech.Server.Auth` | `DeviceService`、トークン、`AuthFilter` | 未着手 |
| `VolTech.Server.Transaction` | Npgsql、冪等性、ゲームログ、`IShardResolver` | 未着手 |
| `VolTech.Server.Logging` | ZLogger、`RpcLogFilter` | 未着手 |
| `VolTech.Server.Hosting` | `AddVoltechServer()`。フィルターの順序を固定する | 未着手 |
| Unity クライアント(UPM) | `ApiCore` の MagicOnion 版 | 未着手 |

## 先に確かめること

実装に入る前に、最小のサーバーとクライアントで次を確かめる。

- [ ] サーバーのフィルターで `RpcException(status, trailers)` を投げたとき、`grpc-status-details-bin` がクライアントまで届くか
- [ ] クライアントで、呼び出しごとのヘッダーと締め切りを渡し、レスポンスヘッダー(`x-server-time` など)を読めるか
- [ ] iOS の IL2CPP(AOT)で、MagicOnion のクライアントと MessagePack が動くか
- [ ] 使う版の MagicOnion で、サーバーフィルターの API 名(`GlobalFilters`・`ServiceContext.Items`)が合っているか
