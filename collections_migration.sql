-- ─── Story Collections Migration ────────────────────────────────────────────
-- Run: psql -d <your_db> -f collections_migration.sql

-- 1. story_collections table
CREATE TABLE IF NOT EXISTS story_collections (
    id           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    creator_id   UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name         VARCHAR(255) NOT NULL,
    description  TEXT,
    cover_url    VARCHAR(500),
    is_public    BOOLEAN      NOT NULL DEFAULT TRUE,
    total_stories INT         NOT NULL DEFAULT 0,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_story_collections_creator
    ON story_collections(creator_id);

-- 2. Add collection_id FK to stories table
ALTER TABLE stories
    ADD COLUMN IF NOT EXISTS collection_id UUID
        REFERENCES story_collections(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_stories_collection
    ON stories(collection_id)
    WHERE collection_id IS NOT NULL;
