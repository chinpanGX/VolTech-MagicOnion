-- プレイヤーごとに、冪等性の対象の RPC の最新 1 件のレスポンス(MessagePack)を持つ。
-- 基盤はアプリの players テーブルを知らないので、外部キーは張らない
CREATE TABLE last_responses (
    player_id  uuid PRIMARY KEY,
    request_id text,
    rpc        text,
    response   bytea,
    updated_at timestamptz NOT NULL DEFAULT now()
);
