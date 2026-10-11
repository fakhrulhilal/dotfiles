SELECT 'CREATE ROLE casdoor LOGIN PASSWORD ''casdoor'''
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'casdoor') \gexec

SELECT 'CREATE DATABASE casdoor OWNER casdoor'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'casdoor') \gexec
