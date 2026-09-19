import duckdb
import pytest

from bppanalyzer.accepted_runs import admit_run, population_projection_sql


def test_python_and_duckdb_apply_the_same_accepted_run_policy() -> None:
    values = (
        (" Vanessa ", " Legendary "),
        (" Hero8 ", "Gold"),
        ("UnknownHero", "Gold"),
        ("Vanessa", "UnknownRank"),
        ("Vanessa", None),
    )
    connection = duckdb.connect(":memory:")
    connection.execute(
        "CREATE TABLE fixture_runs (ordinal INTEGER, hero VARCHAR, final_rank VARCHAR)"
    )
    connection.executemany(
        "INSERT INTO fixture_runs VALUES (?, ?, ?)",
        [(ordinal, *value) for ordinal, value in enumerate(values)],
    )

    rows = connection.execute(
        "SELECT hero_norm, final_rank_norm, hero_recognized, "
        "final_rank_recognized, accepted, segment "
        f"FROM ({population_projection_sql('fixture_runs')}) ORDER BY ordinal"
    ).fetchall()

    for (hero, rank), row in zip(values, rows, strict=True):
        expected = admit_run(hero, rank)
        assert row == (
            expected.hero,
            expected.final_rank,
            not expected.unknown_hero,
            not expected.unknown_final_rank,
            expected.accepted,
            expected.segment,
        )


def test_population_projection_accepts_only_a_relation_identifier() -> None:
    with pytest.raises(ValueError, match="simple SQL identifier"):
        population_projection_sql("runs; DROP TABLE runs")
