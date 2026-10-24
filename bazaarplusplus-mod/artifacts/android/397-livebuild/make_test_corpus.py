"""Builds a schema-2 ten-win test corpus for Vanessa from real template ids.

The first eight cards are the board of the evidence run (a7ef4347, day 6, read from the local
history database's latest battle snapshot); the rest are Vanessa cards of an earlier run.
"""
import json
import sys

C = {
    "Shuriken": ("3af3c836-7224-47bc-bc27-dade6f9d210c", 1),
    "Double Barrel": ("3c9fb662-4088-47f6-a927-0b064154bbbb", 2),
    "Burnacuda": ("8f18974c-eef9-4e82-a2d2-7f4e7c67daf8", 1),
    "Figurehead": ("953402e4-1430-48ab-8156-6193c9d8a7d4", 2),
    "Revolver": ("92e7ab18-a035-43c7-a53c-965aeecbe357", 1),
    "Cinders": ("d45bb1dd-5342-4c69-b7fe-f56d3fac7d1f", 1),
    "Darts": ("29f00f8e-e98b-4b7b-a233-4520e7680b84", 1),
    "Spare Change": ("f212afe2-08d1-40e3-978b-762d345bc7e5", 1),
    "Pufferfish": ("91766035-9d5c-49df-b6b1-25ff7328af5b", 2),
    "Sharkclaws": ("edd857da-ac11-4640-bc1a-217ef8385f72", 2),
    "Chocolate Bar": ("1a3d4cde-333c-4936-b147-263d73f6d200", 1),
    "Pearl": ("1312cf29-3dbb-446f-88b2-0d4999e68d78", 1),
    "Chum": ("c7435ab5-dd8b-4c78-a5a4-6b2795dacaf3", 1),
    "Grappling Hook": ("44710a21-395d-4f78-bdaf-e562c544ae91", 1),
    "Piranha": ("840b6bc5-4786-40bc-b864-cc5c2b78641a", 1),
    "Handaxe": ("b02a2b57-da9c-4ad6-88ac-219b3bfec103", 1),
    "Narwhal": ("00e3a7ff-af5e-47ce-a2c0-2d4ec31ac7d7", 1),
    "Zoarcid": ("3191df3b-7e7d-4df9-9395-6fffd4207f6d", 1),
    "Catfish": ("b325d26f-0625-406c-89d4-30e011b30b9e", 1),
    "Tortuga": ("f8a38ad1-5e5a-4c95-9bd1-55c81c31b117", 3),
    "Tropical": ("4da45ad1-6793-458c-9c72-d03fa273ebfd", 3),
    "Sharkra": ("2834ede9-01e9-4b68-8f1f-70f83187c78f", 2),
}
names = list(C)
cards = [C[n][0] for n in names]
enchantments = [None, "Golden", "Shiny"]

BUILDS = [
    # (items in slot order with tier and enchant ref, stats). Revolver alone matches two builds,
    # Revolver with Burnacuda one.
    ([("Shuriken", 4, 0), ("Double Barrel", 4, 1), ("Burnacuda", 3, 0), ("Figurehead", 3, 0),
      ("Revolver", 4, 2), ("Cinders", 3, 0), ("Darts", 4, 0), ("Spare Change", 3, 0)],
     [412, 61, 1480, 12, 812345]),
    ([("Double Barrel", 4, 0), ("Revolver", 4, 0), ("Darts", 3, 0), ("Pufferfish", 3, 0),
      ("Sharkclaws", 4, 0), ("Chocolate Bar", 3, 0), ("Pearl", 3, 1)],
     [288, 37, 1285, 13, 640120]),
    ([("Burnacuda", 3, 0), ("Shuriken", 4, 0), ("Tortuga", 4, 2), ("Sharkra", 3, 0),
      ("Grappling Hook", 4, 0), ("Piranha", 3, 0), ("Handaxe", 3, 0)],
     [197, 22, 1117, 14, 503377]),
]

builds = []
index = {}
for build_id, (items, stats) in enumerate(BUILDS):
    slot = 0
    layout = []
    refs = []
    for name, tier, ench in items:
        ref = names.index(name)
        size = C[name][1]
        layout.append([ref, slot, tier, ench, size])
        refs.append(ref)
        index.setdefault(ref, []).append(build_id)
        slot += size
    assert slot == 10, (build_id, slot)
    builds.append([sorted(refs), layout, stats])

corpus = {
    "schema_version": 2,
    "kind": "ten_win_builds",
    "generated_at": "2026-10-24T12:00:00Z",
    "window": {"start": "2026-10-20", "end": "2026-10-24", "days": 5},
    "cards": cards,
    "enchantments": enchantments,
    "schemas": {
        "build": ["card_refs", "layout", "stats"],
        "layout": ["card_ref", "slot", "tier", "enchant_ref", "size"],
        "stats": ["completed_run_count", "ten_win_run_count", "ten_win_rate_bps",
                  "p75_ten_win_final_day", "score"],
    },
    # The analyzer's builds.schema.json requires every hero; only Vanessa has builds here.
    "heroes": {
        hero: {"builds": [], "card_index": []}
        for hero in ("Dooley", "Jules", "Karnok", "Mak", "Pygmalien", "Stelle", "TheDragons")
    }
    | {
        "Vanessa": {
            "builds": builds,
            "card_index": [[ref, sorted(set(ids))] for ref, ids in sorted(index.items())],
        }
    },
}
json.dump(corpus, open(sys.argv[1], "w"), separators=(",", ":"))
print("builds", len(builds), "cards", len(cards))
