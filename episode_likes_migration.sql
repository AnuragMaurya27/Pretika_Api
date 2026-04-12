-- ── Episode Likes Table Migration ────────────────────────────────────────────
-- Run this once against the KathaVerse PostgreSQL database.
-- Required by: LikeEpisodeAsync, UnlikeEpisodeAsync, GetEpisodeAsync (IsLiked)

CREATE TABLE IF NOT EXISTS episode_likes (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    episode_id  UUID NOT NULL REFERENCES episodes(id) ON DELETE CASCADE,
    story_id    UUID NOT NULL REFERENCES stories(id) ON DELETE CASCADE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_episode_like UNIQUE (user_id, episode_id)
);

CREATE INDEX IF NOT EXISTS idx_episode_likes_episode ON episode_likes(episode_id);
CREATE INDEX IF NOT EXISTS idx_episode_likes_user    ON episode_likes(user_id);

-- Populate total_likes on episodes from existing data if any
-- (safe to run even if table was just created - will be 0 for new table)
UPDATE episodes e
SET total_likes = (
    SELECT COUNT(*) FROM episode_likes el WHERE el.episode_id = e.id
);
