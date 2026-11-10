BEGIN;
SET LOCAL lock_timeout = '5s';

CREATE TABLE IF NOT EXISTS public.password_reset_challenges (
    user_id uuid PRIMARY KEY REFERENCES public.users(id) ON DELETE CASCADE,
    id uuid NOT NULL UNIQUE,
    code_hash varchar(64),
    code_expires_at timestamptz NOT NULL,
    sent_at timestamptz NOT NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 5),
    password_stamp varchar(64) NOT NULL,
    verified_at timestamptz,
    reset_token_hash varchar(64) UNIQUE,
    reset_expires_at timestamptz,
    CONSTRAINT password_reset_challenges_stage_check CHECK (
        (verified_at IS NULL AND code_hash IS NOT NULL AND reset_token_hash IS NULL AND reset_expires_at IS NULL)
        OR (verified_at IS NOT NULL AND code_hash IS NULL AND reset_token_hash IS NOT NULL AND reset_expires_at IS NOT NULL)
    )
);

ALTER TABLE public.password_reset_challenges ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.password_reset_challenges FROM PUBLIC;
DO $permissions$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        REVOKE ALL ON public.password_reset_challenges FROM anon;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        REVOKE ALL ON public.password_reset_challenges FROM authenticated;
    END IF;
END;
$permissions$;

COMMIT;
