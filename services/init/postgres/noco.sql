SELECT 'CREATE ROLE noco LOGIN PASSWORD ''noco'''
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'noco') \gexec

SELECT 'CREATE DATABASE noco OWNER noco'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'noco') \gexec
