-- Rootprint (OTLP logs/traces backend) user and database; idempotent, safe to re-run
SELECT 'CREATE ROLE rootprint LOGIN PASSWORD ''rootprint'''
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rootprint') \gexec

SELECT 'CREATE DATABASE rootprint OWNER rootprint'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'rootprint') \gexec
