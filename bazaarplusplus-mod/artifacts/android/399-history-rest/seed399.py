"""Seed #399 evidence rows into a pulled History database (never sealed or uploaded).

- seed399-eligible: a completed Unranked, history_only Run of four battles. Not the Sealable
  shape, so History may delete it.
- seed399-protected: a completed Ranked bundle Run with a 'waiting' seal job whose input deadline
  is 2099 and no player account anywhere (run, job, battles). The Run reads as Seal-protected;
  BundleSealCoordinator.TrySealAsync waits on the PlayerAccount gate, so nothing is composed.
Battles copy the device's one real snapshot with variations, with no names or account ids.
Usage: seed399.py DB [--remove]
"""
import json
import sqlite3
import sys

db = sys.argv[1]
con = sqlite3.connect(db)
con.execute("PRAGMA foreign_keys=ON")
cur = con.cursor()

if "--remove" in sys.argv:
    cur.execute("DELETE FROM favorite_battles WHERE battle_id LIKE 'seed399-%'")
    cur.execute("DELETE FROM battle_snapshots WHERE battle_id LIKE 'seed399-%'")
    cur.execute("DELETE FROM battles WHERE battle_id LIKE 'seed399-%'")
    cur.execute("DELETE FROM bundle_seal_jobs WHERE run_id LIKE 'seed399-%'")
    cur.execute("DELETE FROM runs WHERE run_id LIKE 'seed399-%'")
    con.commit()
    print("removed")
    sys.exit(0)

ph, ps, oh, os_ = cur.execute(
    "SELECT player_hand_json, player_skills_json, opponent_hand_json, opponent_skills_json "
    "FROM battle_snapshots LIMIT 1"
).fetchone()


def trim(js, keep):
    data = json.loads(js)
    data["items"] = data["items"][:keep]
    return json.dumps(data)


def count(js):
    return len(json.loads(js)["items"])


BASE = 1792680000000
HOUR = 3600 * 1000
runs = [
    # run_id, hero, mode, policy, rank, final_day, wins, losses
    ("seed399-eligible", "Dooley", "Unranked", "history_only", "Silver", 12, 6, 6),
    ("seed399-protected", "Vanessa", "Ranked", "bundle", "Gold", 2, 1, 1),
]
# Filler archive rows so the list drag-scrolls: Unranked, history_only, no battles.
for n, hero in enumerate(["Pygmalien", "Mak", "Stelle", "Jules", "Dooley", "Pygmalien", "Mak", "Vanessa"]):
    runs.append((f"seed399-filler{n + 1}", hero, "Unranked", "history_only", "Bronze", 3 + n, n % 4, 3))
for i, (rid, hero, mode, policy, rank, day, wins, losses) in enumerate(runs):
    start = BASE - i * 6 * HOUR
    end = start + 4 * HOUR
    cur.execute(
        "INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode, "
        "player_rank, player_rating, day, hour, ended_at_ms, final_day, final_hour, victories, "
        "losses, final_player_rank, final_player_rating, final_player_rating_delta, reason, "
        "build_channel, player_account_id, bundle_screenshot_requested, mod_version, upload_policy) "
        "VALUES (?,?,?,'completed',?,?,?,NULL,?,6,?,?,6,?,?,?,NULL,NULL,'seed399','Online',NULL,0,"
        "'6.0.0.prod',?)",
        (rid, start, end, hero, mode, rank, day, end, day, wins, losses, rank, policy),
    )

variants = [
    (ph, ps, oh, os_),
    (oh, os_, ph, ps),
    (trim(ph, max(1, count(ph) - 2)), ps, oh, os_),
    (ph, ps, trim(oh, 3), os_),
]
battles = [
    ("seed399-eligible", 12, "Dooley", "Pygmalien"),
    ("seed399-protected", 2, "Vanessa", "Mak"),
]
for rid, n, hero, opp in battles:
    start = BASE - (0 if rid.endswith("eligible") else 6) * HOUR
    for k in range(n):
        bid = f"{rid}-b{k + 1}"
        won = k % 2 == 0
        hand, skills, ohand, oskills = variants[k % len(variants)]
        cur.execute(
            "INSERT INTO battles (battle_id, source, run_id, recorded_at_ms, day, hour, "
            "combat_kind, player_name, player_hero, player_rank, opponent_name, opponent_hero, "
            "opponent_rank, result, winner_combatant_id, is_final_battle, local_payload_state, "
            "player_hand_item_count, opponent_hand_item_count) "
            "VALUES (?,'LOCAL',?,?,?,6,'PVPCombat','Seed Player',?,'Silver',?,?,'Silver',?,?,?,"
            "'missing',?,?)",
            (
                bid,
                rid,
                start + (k + 1) * 30 * 60 * 1000,
                k + 1,
                hero,
                f"Seed Rival {k + 1}",
                opp,
                "win" if won else "loss",
                "Player" if won else "Opponent",
                1 if k == n - 1 else 0,
                count(hand),
                count(ohand),
            ),
        )
        cur.execute(
            "INSERT INTO battle_snapshots VALUES (?,?,?,?,?)",
            (bid, hand, skills, ohand, oskills),
        )

# Ghost rows for the Ghost filters, under the device's own account (read in SQL, never printed).
# 'unavailable_payload': nothing is downloaded for them.
for k, (day, won) in enumerate([(3, True), (7, False), (11, True), (12, False)]):
    bid = f"seed399-ghost{k + 1}"
    hand, skills, ohand, oskills = variants[k % len(variants)]
    cur.execute(
        "INSERT INTO battles (battle_id, source, remote_battle_id, uploader_account_id, "
        "local_player_account_id, recorded_at_ms, day, hour, combat_kind, player_name, "
        "player_hero, opponent_name, opponent_hero, result, winner_combatant_id, "
        "ghost_replay_state, ghost_replay_unavailable_reason, bundle_id, download_url, "
        "download_url_expires_at_ms) "
        "VALUES (?,'GHOST',?,'seed399-uploader',"
        "(SELECT player_account_id FROM runs WHERE player_account_id IS NOT NULL "
        "AND run_id NOT LIKE 'seed399-%' LIMIT 1),?,?,3,'PVPCombat','Seed Challenger',?,"
        "'You',?,?,?,'unavailable_payload','seed399',?,'https://invalid.invalid/seed399',4070908800000)",
        (
            bid,
            f"seed399-remote{k + 1}",
            BASE + 12 * HOUR + k * HOUR,
            day,
            ["Jules", "Stelle", "Mak", "Pygmalien"][k],
            "Vanessa",
            "win" if won else "loss",
            "Player" if won else "Opponent",
            f"seed399-bundle{k + 1}",
        ),
    )

cur.execute(
    "INSERT INTO bundle_seal_jobs (run_id, state, player_account_id, screenshot_requested, "
    "screenshot_state, input_deadline_at_ms, created_at_ms) "
    "VALUES ('seed399-protected','waiting',NULL,0,'not_requested',4070908800000,?)",
    (BASE + 10 * HOUR,),
)
con.commit()
print(cur.execute("SELECT run_id, hero, game_mode, status FROM runs").fetchall())
print(cur.execute("SELECT run_id, source, count(*) FROM battles GROUP BY run_id, source").fetchall())
print(cur.execute("SELECT count(*) FROM battles WHERE source='GHOST' AND local_player_account_id IS NULL").fetchall())
