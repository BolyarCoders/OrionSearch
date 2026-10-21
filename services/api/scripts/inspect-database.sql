-- Run with psql using an authorized connection supplied through the environment.
-- This script does not initialize, migrate, insert, update or delete anything.
\set ON_ERROR_STOP on
BEGIN READ ONLY;
SET LOCAL statement_timeout = '10s';

SELECT current_database() AS database, current_user AS role, current_setting('server_version') AS postgres_version;
SELECT nspname AS schema FROM pg_namespace
WHERE nspname IN ('auth', 'research', 'content', 'knowledge', 'ai', 'publishing', 'system') ORDER BY nspname;
SELECT extname, extversion FROM pg_extension WHERE extname = 'citext';
SELECT schemaname, tablename FROM pg_tables
WHERE schemaname NOT IN ('pg_catalog', 'information_schema') ORDER BY schemaname, tablename;

SELECT to_regclass('system.__ef_migrations_history') IS NOT NULL AS has_history \gset
\if :has_history
SELECT "MigrationId", "ProductVersion" FROM system.__ef_migrations_history ORDER BY "MigrationId";
\else
\echo No OrionSearch migration history exists.
\endif

-- Counts reveal whether this is an empty destination without returning application records.
SELECT format('SELECT %L AS table_name, count(*) AS row_count FROM %I.%I',
    schemaname || '.' || tablename, schemaname, tablename)
FROM pg_tables WHERE schemaname IN ('auth', 'research', 'content', 'knowledge', 'ai', 'publishing', 'system')
ORDER BY schemaname, tablename \gexec

SELECT n.nspname AS schema, t.relname AS table_name, c.conname AS constraint_name,
    pg_get_constraintdef(c.oid) AS definition
FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid
JOIN pg_namespace n ON n.oid = t.relnamespace
WHERE n.nspname IN ('auth', 'research', 'content', 'knowledge', 'system')
ORDER BY n.nspname, t.relname, c.conname;
COMMIT;
