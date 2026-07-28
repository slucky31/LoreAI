CREATE TABLE IF NOT EXISTS PollingState (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    LastRaindropId INTEGER,
    LastCreatedUtc TEXT,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Articles (
    Id INTEGER PRIMARY KEY,
    Title TEXT NOT NULL,
    Link TEXT NOT NULL,
    Excerpt TEXT,
    Note TEXT,
    OriginalTags TEXT,
    CollectionId INTEGER,
    Domain TEXT,
    RaindropType TEXT,
    RaindropCreatedUtc TEXT NOT NULL,
    RaindropLastUpdateUtc TEXT,
    FetchedAtUtc TEXT NOT NULL,

    SuggestedCollection TEXT,
    SuggestedTags TEXT,
    RecommendedAction TEXT NOT NULL,
    Priority TEXT NOT NULL,
    Reason TEXT,
    ClassificationModel TEXT,
    ClassificationRawResponse TEXT,
    ClassifiedAtUtc TEXT,

    Moved INTEGER NOT NULL DEFAULT 0,
    WriteBackStatus TEXT,
    WriteBackAtUtc TEXT,
    DiscordNotifiedAtUtc TEXT,
    EmailDigestSentAtUtc TEXT
);

CREATE INDEX IF NOT EXISTS idx_articles_digest_pending ON Articles (EmailDigestSentAtUtc);
CREATE INDEX IF NOT EXISTS idx_articles_created ON Articles (RaindropCreatedUtc);
