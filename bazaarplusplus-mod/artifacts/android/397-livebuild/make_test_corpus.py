"""Builds a schema-2 ten-win test corpus for Vanessa from real template ids in the current run."""
import json
import sys

C = {
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
    # (items in slot order with tier and enchant ref, stats)
    ([("Pufferfish", 4, 0), ("Sharkclaws", 4, 1), ("Chocolate Bar", 3, 0), ("Pearl", 3, 0),
      ("Chum", 3, 0), ("Grappling Hook", 4, 2), ("Piranha", 3, 0), ("Handaxe", 4, 0)],
     [412, 61, 1480, 12, 812345]),
    ([("Tortuga", 4, 0), ("Sharkclaws", 4, 0), ("Piranha", 4, 0), ("Narwhal", 3, 0),
      ("Catfish", 3, 0), ("Zoarcid", 3, 0), ("Handaxe", 3, 1)],
     [288, 37, 1285, 13, 640120]),
    ([("Tropical", 3, 0), ("Sharkra", 4, 0), ("Pufferfish", 4, 2), ("Grappling Hook", 3, 0),
      ("Pearl", 4, 0), ("Chocolate Bar", 3, 0)],
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
    "generated_at": "2026-10-23T02:00:00Z",
    "window": {"start": "2026-10-19", "end": "2026-10-23", "days": 5},
    "cards": cards,
    "enchantments": enchantments,
    "schemas": {
        "build": ["card_refs", "layout", "stats"],
        "layout": ["card_ref", "slot", "tier", "enchant_ref", "size"],
        "stats": ["completed_run_count", "ten_win_run_count", "ten_win_rate_bps",
                  "p75_ten_win_final_day", "score"],
    },
    "heroes": {
        "Vanessa": {
            "builds": builds,
            "card_index": [[ref, sorted(set(ids))] for ref, ids in sorted(index.items())],
        }
    },
}
json.dump(corpus, open(sys.argv[1], "w"), separators=(",", ":"))
print("builds", len(builds), "cards", len(cards))
