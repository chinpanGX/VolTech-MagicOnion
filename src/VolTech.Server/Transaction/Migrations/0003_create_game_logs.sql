-- ゲームログ(所持データの変化の履歴)。種別ごとにテーブルを分けず、種別で形の変わりうる値は body の JSON に入れる。
-- 基盤はアプリの players テーブルを知らないので、外部キーは張らない
CREATE TABLE game_logs (
    log_id     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    player_id  uuid NOT NULL,
    request_id text,
    rpc        text NOT NULL,
    kind       text NOT NULL,
    body       jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- 問い合わせで、プレイヤーの履歴を時刻の順に引く
CREATE INDEX idx_game_logs_player_created ON game_logs (player_id, created_at);
