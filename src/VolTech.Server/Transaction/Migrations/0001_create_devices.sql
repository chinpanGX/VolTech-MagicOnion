-- デバイスと、デバイス登録で発行したプレイヤーの ID。プレイヤー(初期データ)はアプリのサインアップで作る
CREATE TABLE devices (
    device_id   text PRIMARY KEY,
    player_id   uuid NOT NULL UNIQUE,
    secret_hash text NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- デバイスごとに最新のアクセストークンを 1 つだけ持つ。発行し直すと上書きし、古いトークンは無効になる
CREATE TABLE access_tokens (
    device_id  text PRIMARY KEY REFERENCES devices (device_id),
    token      text NOT NULL UNIQUE,
    expires_at timestamptz NOT NULL
);
