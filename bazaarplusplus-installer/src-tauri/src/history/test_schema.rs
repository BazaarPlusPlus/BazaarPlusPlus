//! The local history database the installer reads, built from the mod's schema
//! golden. The mod owns the schema (`RunLogSchema`) and `RunLoggingPipeline.Tests`
//! dumps it from a freshly initialized database, so a mod column change fails
//! installer tests instead of passing against a hand-copied shape.

const MOD_SCHEMA_GOLDEN: &str = include_str!(
    "../../../../bazaarplusplus-mod/tests/RunLoggingPipeline.Tests/fixtures/history-database-v3.schema.sql"
);

/// Creates every table, index, and trigger of the mod's current local history
/// database and sets its `user_version`, with foreign keys on as the mod opens it.
pub(crate) fn create_mod_schema(conn: &rusqlite::Connection) {
    conn.execute_batch(MOD_SCHEMA_GOLDEN).unwrap();
    let user_version: i64 = conn
        .query_row("pragma user_version", [], |row| row.get(0))
        .unwrap();
    assert_eq!(
        Some(&user_version),
        crate::config::supported_mod_db_user_versions().last(),
        "the mod schema golden's user_version must be the newest version \
         history-database-compatibility.json supports"
    );
    conn.execute_batch("pragma foreign_keys = on;").unwrap();
}
