//! The mod's local history database schema, from the mod's schema file. The mod
//! owns the schema (`RunLogSchema`) and `RunLoggingPipeline.Tests` dumps it from a
//! freshly initialized database, so a mod column change fails installer tests
//! instead of passing against a hand-copied shape. The V5 import creates a
//! missing current database from it; tests build their databases from it.

use anyhow::{bail, Context as _};

const MOD_SCHEMA_SQL: &str = include_str!(
    "../../../../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/history-database-v6.schema.sql"
);

/// Creates every table, index, and trigger of the mod's current local history
/// database on an empty connection and sets its `user_version`. Runs inside the
/// caller's transaction when one is open.
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

/// [`execute_mod_schema`] with foreign keys on, as the mod opens the database.
#[cfg(test)]
pub(crate) fn create_mod_schema(conn: &rusqlite::Connection) {
    execute_mod_schema(conn).unwrap();
    conn.execute_batch("pragma foreign_keys = on;").unwrap();
}

/// The schema text as `sqlite_master` stores it, in the mod golden's dump format,
/// so a database can be compared with the file byte for byte.
#[cfg(test)]
pub(crate) fn mod_schema_sql() -> &'static str {
    MOD_SCHEMA_SQL
}

/// Dumps `conn`'s schema in the mod golden's format: `PRAGMA user_version=N;`, then
/// every `sqlite_master` statement ordered by type (table, index, trigger) and name.
#[cfg(test)]
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
