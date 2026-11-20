//! Use the mod's schema so installer tests fail on incompatible changes to the writer's actual columns.

use anyhow::{bail, Context as _};

const MOD_SCHEMA_SQL: &str = include_str!(
    "../../../../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/history-database-v6.schema.sql"
);

pub(crate) fn execute_mod_schema(conn: &rusqlite::Connection) -> anyhow::Result<()> {
    conn.execute_batch(MOD_SCHEMA_SQL)
        .context("Cannot create the history database schema")?;
    let user_version: i64 = conn.query_row("pragma user_version", [], |row| row.get(0))?;
    if Some(&user_version) != crate::config::supported_mod_db_user_versions().last() {
        bail!(
            "The mod schema file sets user_version {user_version}, which is not the newest \
             version history-database-compatibility.json supports"
        );
    }
    Ok(())
}

pub(crate) fn create_mod_schema(conn: &rusqlite::Connection) {
    execute_mod_schema(conn).unwrap();
    conn.execute_batch("pragma foreign_keys = on;").unwrap();
}

pub(crate) fn mod_schema_sql() -> &'static str {
    MOD_SCHEMA_SQL
}

pub(crate) fn dump_schema(conn: &rusqlite::Connection) -> String {
    let user_version: i64 = conn
        .query_row("pragma user_version", [], |row| row.get(0))
        .unwrap();
    let mut schema = format!("PRAGMA user_version={user_version};\n");
    let mut statement = conn
        .prepare(
            "select sql from sqlite_master where sql is not null and name not like 'sqlite_%' \
             order by case type when 'table' then 0 when 'index' then 1 when 'trigger' then 2 \
             else 3 end, name",
        )
        .unwrap();
    let rows = statement
        .query_map([], |row| row.get::<_, String>(0))
        .unwrap();
    for sql in rows {
        schema.push_str(&sql.unwrap().replace("\r\n", "\n"));
        schema.push_str(";\n");
    }
    schema
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_database_built_from_the_schema_file_dumps_back_to_it() {
        let conn = rusqlite::Connection::open_in_memory().unwrap();
        execute_mod_schema(&conn).unwrap();
        assert_eq!(dump_schema(&conn), mod_schema_sql());
    }
}
