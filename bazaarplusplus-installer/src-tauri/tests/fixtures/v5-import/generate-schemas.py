#!/usr/bin/env python3
"""Regenerates the frozen V5 schema fixtures in this directory from pinned commits.

Run from anywhere inside the repository: `python3 generate-schemas.py`. It reads only
git objects at the pinned commits below, never the working tree, so it keeps working
after the V5 migration code is deleted. `--check` compares instead of writing.

Every fixture is a dump in the format of the mod's history database golden:
`PRAGMA user_version=N;`, then each `sqlite_master` statement ordered by type
(table, index, trigger) and name, each ending in `;` and LF.
"""

import pathlib
import re
import sqlite3
import subprocess
import sys

SCHEMA_PATH = "bazaarplusplus-mod/src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs"
V3_GOLDEN_PATH = (
    "bazaarplusplus-mod/tests/RunLoggingPipeline.Tests/fixtures/history-database-v3.schema.sql"
)
# The last commit whose RunLogSchema had LocalDatabaseSchemaVersion = 1.
V1_COMMIT = "d16fee6192f4539f7c853cdd10a653fb2d76df8d"
# Tag 5.5.0: LocalDatabaseSchemaVersion = 2.
V2_COMMIT = "c5c9c919e08b79ce0e9621f10681e679d19e97c4"
# master when the fixtures were frozen: LocalDatabaseSchemaVersion = 3, still carrying
# UpgradeToLifecycleColumns and the v3 golden under its current name.
V3_COMMIT = "3c8e50556b97e059ef8f1eb56c552e3b9437603e"

HERE = pathlib.Path(__file__).resolve().parent


def git_show(commit, path):
    return subprocess.run(
        ["git", "show", f"{commit}:{path}"],
        cwd=HERE,
        check=True,
        capture_output=True,
    ).stdout.decode("utf-8")  # bytes: text mode would turn the lone CR in HistoryWhitespace into LF


def decode_csharp_string(literal):
    """Decodes the body of a regular C# string literal (the escapes RunLogSchema uses)."""
    simple = {"t": "\t", "n": "\n", "v": "\v", "f": "\f", "r": "\r", "0": "\0",
              "'": "'", '"': '"', "\\": "\\"}
    out, i = [], 0
    while i < len(literal):
        ch = literal[i]
        if ch != "\\":
            out.append(ch)
            i += 1
            continue
        code = literal[i + 1]
        if code == "u":
            out.append(chr(int(literal[i + 2:i + 6], 16)))
            i += 6
        elif code in simple:
            out.append(simple[code])
            i += 2
        else:
            raise ValueError(f"unsupported C# escape \\{code}")
    return "".join(out)


def string_constants(source):
    """Evaluates every `const string X = "..." + Y + "...";` in declaration order."""
    constants = {}
    pattern = re.compile(r"const string (\w+)\s*=\s*(.*?);\s*$", re.S | re.M)
    for name, expression in pattern.findall(source):
        value = []
        for token in re.findall(r'"(?:[^"\\]|\\.)*"|\w+', expression):
            if token.startswith('"'):
                value.append(decode_csharp_string(token[1:-1]))
            else:
                value.append(constants[token])
        constants[name] = "".join(value)
    for name, number in re.findall(r"const int (\w+)\s*=\s*(\d+);", source):
        constants[name] = number
    return constants


def raw_interpolated(source, start, constants):
    """Renders the `$\"\"\"...\"\"\"` raw interpolated string that begins after `start`."""
    begin = source.index('$"""', source.index(start)) + 4
    end = source.index('"""', begin)
    body = source[begin:end]
    assert body.startswith("\n"), "raw string must start on its own line"
    lines = body[1:].split("\n")
    closing_indent = lines.pop()
    assert closing_indent.strip() == "", "closing quotes must be on their own line"
    dedented = []
    for line in lines:
        if line.strip():
            assert line.startswith(closing_indent), line
            dedented.append(line[len(closing_indent):])
        else:
            dedented.append("")
    text = "\n".join(dedented)
    return re.sub(r"\{(\w+)\}", lambda m: constants[m.group(1)], text)


def schema_at(commit):
    source = git_show(commit, SCHEMA_PATH)
    return source, string_constants(source)


def execute(conn, sql):
    conn.executescript("BEGIN IMMEDIATE;\n" + sql + "\nCOMMIT;")


def user_version(conn):
    return conn.execute("PRAGMA user_version;").fetchone()[0]


def dump(conn):
    rows = conn.execute(
        """
        SELECT sql FROM sqlite_master
        WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%'
        ORDER BY CASE type WHEN 'table' THEN 0 WHEN 'index' THEN 1 WHEN 'trigger' THEN 2 ELSE 3 END,
            name;
        """
    ).fetchall()
    text = f"PRAGMA user_version={user_version(conn)};\n"
    for (sql,) in rows:
        text += sql.replace("\r\n", "\n") + ";\n"
    return text


def open_db():
    return sqlite3.connect(":memory:", isolation_level=None)


def fresh(bootstrap_sql):
    conn = open_db()
    execute(conn, bootstrap_sql)
    return conn


def index_definitions(conn):
    return dict(
        conn.execute(
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL;"
        ).fetchall()
    )


def upgrade_v1_with_master(conn):
    """Replays RunLogSchema.EnsureInitialized at V3_COMMIT on a user_version 1 database.

    UpgradeToLifecycleColumns runs its raw SQL; EnsureInitialized then runs
    DropDriftedIndexes and BootstrapSql. ValidateLifecycleColumns only reads, and
    RecoverLegacyJsonFailures / NormalizeSealJobDeadlines only UPDATE rows, so none of
    them changes the shape this dump records.
    """
    source, constants = schema_at(V3_COMMIT)
    upgrade_sql = raw_interpolated(source, "private static void UpgradeToLifecycleColumns", constants)
    bootstrap_sql = raw_interpolated(source, "public static string BootstrapSql", constants)

    assert user_version(conn) == int(constants["FirstSchemaVersion"])
    execute(conn, upgrade_sql)
    assert user_version(conn) == int(constants["LifecycleColumnsSchemaVersion"])

    expected = index_definitions(fresh(bootstrap_sql))
    drifted = [
        name
        for name, stored in index_definitions(conn).items()
        if name in expected and expected[name] != stored
    ]
    execute(conn, "".join(f'DROP INDEX "{name}";\n' for name in drifted) + bootstrap_sql)
    assert user_version(conn) == int(constants["LocalDatabaseSchemaVersion"])
    return drifted


def build():
    v1_source, v1_constants = schema_at(V1_COMMIT)
    assert v1_constants["LocalDatabaseSchemaVersion"] == "1"
    v1_bootstrap = raw_interpolated(v1_source, "public static string BootstrapSql", v1_constants)
    v1 = fresh(v1_bootstrap)

    v2_source, v2_constants = schema_at(V2_COMMIT)
    assert v2_constants["LocalDatabaseSchemaVersion"] == "2"
    v2 = fresh(raw_interpolated(v2_source, "public static string BootstrapSql", v2_constants))

    v3_source, v3_constants = schema_at(V3_COMMIT)
    assert v3_constants["LocalDatabaseSchemaVersion"] == "3"
    v3 = fresh(raw_interpolated(v3_source, "public static string BootstrapSql", v3_constants))
    v3_dump = dump(v3)
    golden = git_show(V3_COMMIT, V3_GOLDEN_PATH)
    assert v3_dump == golden, "rendered BootstrapSql does not reproduce the v3 golden"

    upgraded = fresh(v1_bootstrap)
    drifted = upgrade_v1_with_master(upgraded)
    assert drifted == [], f"unexpected drifted indexes: {drifted}"

    return {
        "v1.schema.sql": dump(v1),
        "v2.schema.sql": dump(v2),
        "v3.schema.sql": golden,
        "v3-upgraded-from-v1.schema.sql": dump(upgraded),
    }


def main():
    check = "--check" in sys.argv[1:]
    stale = []
    for name, text in build().items():
        path = HERE / name
        if check:
            if not path.exists() or path.read_bytes() != text.encode("utf-8"):
                stale.append(name)
        else:
            path.write_bytes(text.encode("utf-8"))
    if stale:
        sys.exit(f"stale: {', '.join(stale)}")


if __name__ == "__main__":
    main()
