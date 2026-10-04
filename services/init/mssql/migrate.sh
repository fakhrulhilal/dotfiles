#!/bin/bash

SCRIPT_ROOT=$(cd "$(dirname "$0")" && pwd)
DB_PORT=${DB_PORT:-1433}
DB_USERNAME=${DB_USERNAME:-sa}
SQLCMD=$(command -v sqlcmd || echo /opt/mssql-tools18/bin/sqlcmd)

if [[ -z "$DB_HOST" || -z "$DB_PASSWORD" ]]
then
  echo "DB_HOST and DB_PASSWORD are required"
  exit 1
fi

run_sql() {
  "$SQLCMD" -S "$DB_HOST,$DB_PORT" -U "$DB_USERNAME" -P "$DB_PASSWORD" -C -b -d "$1" -i "$2"
}

echo "Start migration from $SCRIPT_ROOT to server $DB_HOST,$DB_PORT"
for sql_file in "$SCRIPT_ROOT"/*.sql
do
  [[ -f "$sql_file" ]] || continue
  echo "Executing: $(basename "$sql_file")"
  run_sql master "$sql_file" || exit 1
done

for db_dir in "$SCRIPT_ROOT"/*/
do
  [[ -d "$db_dir" ]] || continue
  db_name=$(basename "$db_dir")
  echo "Migrating database '$db_name'"
  for sql_file in "$db_dir"*.sql
  do
    [[ -f "$sql_file" ]] || continue
    echo "Executing: $(basename "$sql_file")"
    run_sql "$db_name" "$sql_file" || exit 1
  done
done