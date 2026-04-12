-- ═══════════════════════════════════════════════════════════════════════════
--  DARR ARENA — Full Schema Migration
-- ═══════════════════════════════════════════════════════════════════════════

-- ENUMS
DO $$ BEGIN
    CREATE TYPE arena_event_status AS ENUM (
        'upcoming', 'writing', 'review', 'completed', 'cancelled'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE arena_participant_status AS ENUM (
        'active', 'disqualified', 'refunded', 'withdrew'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE arena_story_status AS ENUM (
        'draft', 'submitted', 'locked', 'disqualified'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE arena_assignment_status AS ENUM (
        'pending', 'in_progress', 'completed', 'defaulted', 'disqualified'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE arena_badge_type AS ENUM (
        'champion', 'runner_up', 'second_runner_up', 'participant'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- ─── EVENTS ──────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_events (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title                       VARCHAR(200)             NOT NULL,
    description                 TEXT                     NOT NULL,
    topic                       TEXT                     NOT NULL,
    story_type                  VARCHAR(20)              NOT NULL DEFAULT 'short',
    min_word_limit              INT                      NOT NULL DEFAULT 500,
    max_word_limit              INT                      NOT NULL DEFAULT 2000,
    entry_fee_coins             INT                      NOT NULL DEFAULT 50,
    hall_reading_cost_coins     INT                      NOT NULL DEFAULT 10,
    writing_phase_hours         INT                      NOT NULL DEFAULT 48,
    review_phase_hours          INT                      NOT NULL DEFAULT 48,
    min_participants_threshold  INT                      NOT NULL DEFAULT 10,
    status                      arena_event_status       NOT NULL DEFAULT 'upcoming',
    writing_phase_starts_at     TIMESTAMPTZ,
    writing_phase_ends_at       TIMESTAMPTZ,
    review_phase_ends_at        TIMESTAMPTZ,
    finalized_at                TIMESTAMPTZ,
    cancelled_at                TIMESTAMPTZ,
    cancelled_reason            TEXT,
    original_prize_pool         BIGINT                   NOT NULL DEFAULT 0,
    prize_pot_live              BIGINT                   NOT NULL DEFAULT 0,
    forfeit_pool                BIGINT                   NOT NULL DEFAULT 0,
    random_seed                 INT,
    winner_announced            BOOLEAN                  NOT NULL DEFAULT FALSE,
    created_by                  UUID                     NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    deleted_at                  TIMESTAMPTZ,
    created_at                  TIMESTAMPTZ              NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMPTZ              NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_arena_events_status     ON arena_events(status);
CREATE INDEX IF NOT EXISTS idx_arena_events_created_at ON arena_events(created_at DESC);

-- ─── PARTICIPANTS ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_participants (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id    UUID                     NOT NULL REFERENCES arena_events(id) ON DELETE CASCADE,
    user_id     UUID                     NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    status      arena_participant_status NOT NULL DEFAULT 'active',
    joined_at   TIMESTAMPTZ              NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ              NOT NULL DEFAULT NOW(),
    UNIQUE (event_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_arena_participants_event ON arena_participants(event_id);
CREATE INDEX IF NOT EXISTS idx_arena_participants_user  ON arena_participants(user_id);

-- ─── STORIES ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_stories (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id            UUID                 NOT NULL REFERENCES arena_events(id) ON DELETE CASCADE,
    author_id           UUID                 NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title               VARCHAR(300)         NOT NULL DEFAULT '',
    content             TEXT                 NOT NULL DEFAULT '',
    word_count          INT                  NOT NULL DEFAULT 0,
    status              arena_story_status   NOT NULL DEFAULT 'draft',
    questions_submitted BOOLEAN              NOT NULL DEFAULT FALSE,
    avg_rating          NUMERIC(4,2),
    total_reviews       INT                  NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ          NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ          NOT NULL DEFAULT NOW(),
    UNIQUE (event_id, author_id)
);

CREATE INDEX IF NOT EXISTS idx_arena_stories_event  ON arena_stories(event_id);
CREATE INDEX IF NOT EXISTS idx_arena_stories_author ON arena_stories(author_id);

-- ─── STORY QUESTIONS ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_story_questions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    story_id        UUID         NOT NULL REFERENCES arena_stories(id) ON DELETE CASCADE,
    question_order  INT          NOT NULL,
    question_text   TEXT         NOT NULL,
    option_a        TEXT         NOT NULL,
    option_b        TEXT         NOT NULL,
    option_c        TEXT         NOT NULL,
    option_d        TEXT         NOT NULL,
    correct_option  CHAR(1)      NOT NULL,  -- 'a' | 'b' | 'c' | 'd'
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (story_id, question_order)
);

-- ─── REVIEW ASSIGNMENTS ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_review_assignments (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id             UUID                     NOT NULL REFERENCES arena_events(id) ON DELETE CASCADE,
    story_id             UUID                     NOT NULL REFERENCES arena_stories(id) ON DELETE CASCADE,
    assigned_to          UUID                     NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    status               arena_assignment_status  NOT NULL DEFAULT 'pending',
    read_started_at      TIMESTAMPTZ,
    read_time_verified   BOOLEAN                  NOT NULL DEFAULT FALSE,
    scroll_verified      BOOLEAN                  NOT NULL DEFAULT FALSE,
    questions_passed     BOOLEAN                  NOT NULL DEFAULT FALSE,
    wrong_attempts       INT                      NOT NULL DEFAULT 0,
    extra_coins_offered  INT                      NOT NULL DEFAULT 0,
    is_extra_review      BOOLEAN                  NOT NULL DEFAULT FALSE,
    completed_at         TIMESTAMPTZ,
    assigned_at          TIMESTAMPTZ              NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ              NOT NULL DEFAULT NOW(),
    UNIQUE (story_id, assigned_to)
);

CREATE INDEX IF NOT EXISTS idx_arena_assignments_event    ON arena_review_assignments(event_id);
CREATE INDEX IF NOT EXISTS idx_arena_assignments_assignee ON arena_review_assignments(assigned_to);
CREATE INDEX IF NOT EXISTS idx_arena_assignments_story    ON arena_review_assignments(story_id);

-- ─── RATINGS ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_ratings (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    assignment_id  UUID         NOT NULL REFERENCES arena_review_assignments(id) ON DELETE CASCADE,
    event_id       UUID         NOT NULL REFERENCES arena_events(id) ON DELETE CASCADE,
    story_id       UUID         NOT NULL REFERENCES arena_stories(id) ON DELETE CASCADE,
    rated_by       UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    rating         NUMERIC(3,1) NOT NULL,
    comment        TEXT,
    rated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (assignment_id)
);

CREATE INDEX IF NOT EXISTS idx_arena_ratings_story ON arena_ratings(story_id);

-- ─── WINNERS ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_winners (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id        UUID         NOT NULL REFERENCES arena_events(id) ON DELETE CASCADE,
    user_id         UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    story_id        UUID         NOT NULL REFERENCES arena_stories(id) ON DELETE CASCADE,
    rank            INT          NOT NULL,
    average_rating  NUMERIC(4,2),
    total_reviews   INT          NOT NULL DEFAULT 0,
    coins_won       BIGINT       NOT NULL DEFAULT 0,
    awarded_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (event_id, rank)
);

-- ─── BADGES ──────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS arena_badges (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id     UUID              NOT NULL REFERENCES arena_events(id) ON DELETE CASCADE,
    user_id      UUID              NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    story_id     UUID              NOT NULL REFERENCES arena_stories(id) ON DELETE CASCADE,
    event_title  VARCHAR(200)      NOT NULL,
    badge_type   arena_badge_type  NOT NULL,
    rank         INT               NOT NULL,
    coins_won    BIGINT            NOT NULL DEFAULT 0,
    awarded_at   TIMESTAMPTZ       NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_arena_badges_user ON arena_badges(user_id);
