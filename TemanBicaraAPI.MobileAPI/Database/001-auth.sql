-- UUID conversion is only safe automatically while the affected tables are empty.
DO $migration$
BEGIN
  PERFORM set_config('lock_timeout', '5s', true);
  LOCK TABLE public.users, public.sessions, public.assets,
    public.class_rooms_students, public.class_rooms_teachers IN ACCESS EXCLUSIVE MODE;
  IF EXISTS (SELECT 1 FROM public.users UNION ALL SELECT 1 FROM public.sessions
    UNION ALL SELECT 1 FROM public.assets UNION ALL SELECT 1 FROM public.class_rooms_students
    UNION ALL SELECT 1 FROM public.class_rooms_teachers) THEN
    RAISE EXCEPTION 'UUID conversion requires empty account tables; existing data needs an explicit mapping.';
  END IF;

  ALTER TABLE public.sessions DROP CONSTRAINT sessions_usersid_fkey;
  ALTER TABLE public.assets DROP CONSTRAINT assets_usersid_fkey;
  ALTER TABLE public.class_rooms_students DROP CONSTRAINT class_rooms_students_studentid_fkey;
  ALTER TABLE public.class_rooms_teachers DROP CONSTRAINT class_rooms_teachers_teachersid_fkey;
  ALTER TABLE public.users ALTER COLUMN id DROP DEFAULT;
  ALTER TABLE public.users ALTER COLUMN id TYPE uuid USING NULL::uuid;
  ALTER TABLE public.users ALTER COLUMN id SET DEFAULT gen_random_uuid();
  ALTER TABLE public.sessions ALTER COLUMN usersid TYPE uuid USING NULL::uuid;
  ALTER TABLE public.assets ALTER COLUMN usersid TYPE uuid USING NULL::uuid;
  ALTER TABLE public.class_rooms_students ALTER COLUMN studentid TYPE uuid USING NULL::uuid;
  ALTER TABLE public.class_rooms_teachers ALTER COLUMN teachersid TYPE uuid USING NULL::uuid;
  ALTER TABLE public.sessions ADD CONSTRAINT sessions_usersid_fkey
    FOREIGN KEY (usersid) REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;
  ALTER TABLE public.assets ADD CONSTRAINT assets_usersid_fkey FOREIGN KEY (usersid) REFERENCES public.users(id);
  ALTER TABLE public.class_rooms_students ADD CONSTRAINT class_rooms_students_studentid_fkey
    FOREIGN KEY (studentid) REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;
  ALTER TABLE public.class_rooms_teachers ADD CONSTRAINT class_rooms_teachers_teachersid_fkey
    FOREIGN KEY (teachersid) REFERENCES public.users(id) ON UPDATE CASCADE ON DELETE CASCADE;

  ALTER TABLE public.users
    ALTER COLUMN email SET NOT NULL,
    ALTER COLUMN password DROP DEFAULT,
    ADD COLUMN auth_version bigint NOT NULL DEFAULT 0 CHECK (auth_version >= 0),
    ADD COLUMN email_verified_at timestamptz,
    ADD COLUMN verification_code_hash text,
    ADD COLUMN verification_expires_at timestamptz,
    ADD COLUMN verification_sent_at timestamptz,
    ADD COLUMN verification_attempts integer NOT NULL DEFAULT 0 CHECK (verification_attempts BETWEEN 0 AND 5);
  CREATE UNIQUE INDEX users_username_normalized_key ON public.users (lower(btrim(user_name)));
  CREATE UNIQUE INDEX users_email_normalized_key ON public.users (lower(btrim(email)));
  ALTER TABLE public.users ADD CONSTRAINT users_password_not_empty CHECK (length(password) > 0);
  ALTER TABLE public.roles ADD CONSTRAINT roles_role_name_key UNIQUE (role_name);
  INSERT INTO public.roles (role_name) VALUES ('Teacher'), ('Student') ON CONFLICT (role_name) DO NOTHING;
END;
$migration$;
