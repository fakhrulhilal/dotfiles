#!/bin/bash

if [[ "$SEED" != "true" && "$SEED" != "1" ]]
then
    echo "Skip migration files"
    exit 1
fi

SCRIPT_ROOT=/migrations
POSTGRES_SERVER=${POSTGRES_SERVER:-localhost}
export PGPASSWORD=${POSTGRES_PASSWORD}

echo "Start migration from $SCRIPT_ROOT to server $POSTGRES_SERVER"
for sql_file in "$SCRIPT_ROOT"/*.sql
do
    [[ -f "$sql_file" ]] || continue
    echo "Executing: $(basename "$sql_file")"
    psql -h $POSTGRES_SERVER -U $POSTGRES_USER -d postgres -f "$sql_file"
done

while IFS= read -r db_name
do
    [[ -d "$SCRIPT_ROOT/$db_name" ]] || continue
    echo "Migrating database '$db_name'"
    for sql_file in $SCRIPT_ROOT/$db_name/*.sql
    do
        echo "Executing: $(basename "$sql_file")"
        psql -h $POSTGRES_SERVER -U $POSTGRES_USER -d $db_name -f "$sql_file"
    done
done < <(ls "$SCRIPT_ROOT" 2> /dev/null)