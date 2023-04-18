CREATE TABLE IF NOT EXISTS public.leaderboards
(
    userid text COLLATE pg_catalog."default" NOT NULL,
    bongs integer,
    username text COLLATE pg_catalog."default",
    CONSTRAINT "Leaderboards_pkey" PRIMARY KEY (userid)
)

TABLESPACE pg_default;

ALTER TABLE IF EXISTS public.leaderboards
    OWNER to postgres;

CREATE TABLE IF NOT EXISTS public.webhooks
(
    guildid text COLLATE pg_catalog."default" NOT NULL,
    webhook text COLLATE pg_catalog."default",
    CONSTRAINT "Webhooks_pkey" PRIMARY KEY (guildid)
)

TABLESPACE pg_default;

ALTER TABLE IF EXISTS public.webhooks
    OWNER to postgres;