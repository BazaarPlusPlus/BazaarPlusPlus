# Consumer data semantics

The strict schemas in `../../contracts/v5/` own JSON field names, types,
required fields, enums, and scalar bounds. This document owns population,
calculation, ordering, and cross-field semantics. Domain terms are defined in
`../../CONTEXT.md`.

The public object set is exactly:

```text
analyzer-v5/heroes/latest.json
analyzer-v5/builds/latest.json
```

Each key holds a mutable current snapshot for one independent consumer.

## Shared population and window

`Hero8` is normalized to `TheDragons`. A Run becomes an Accepted Run only when
its normalized hero and final rank are canonical. The canonical ranks are
`Bronze`, `Silver`, `Gold`, `Diamond`, `Master`, `Masters`, and `Legendary`.
Rejecting a Run also rejects its Battles and card snapshots.

`Legendary` maps to the `legend` segment; every other canonical rank maps to
`non_legend`. A Battle inherits the segment of its owning Run. The `all`
segment is always derived by adding `legend` and `non_legend`.

Both products use the latest sequence of one to seven consecutive sealed
Complete Source Days. A newer incomplete day does not block an earlier sealed
day from ending the window. The window never crosses the optional Source
Epoch. `window.start` and `window.end` are inclusive UTC dates, and
`generated_at` is the UTC snapshot-generation time.

## Hero metrics

`heroes/latest.json` contains one newest-first partition per Source Day. Each
partition has one row for every canonical Hero × stored Segment pair in
canonical hero order, with `legend` before `non_legend`. An unobserved pair has
zero counts and an empty `matchups` array.

Only additive integer components are stored. A consumer selects up to the
newest 1, 3, or 7 available partitions, sums each field by hero and segment,
then derives rates and averages.

### Run outcomes

`runs.completed` counts completed Accepted Runs. `runs.scored` is the subset
whose victories are from 0 through 10 and whose losses are non-negative.

| Outcome | Definition |
| --- | --- |
| `perfect` | 10 victories and 0 losses |
| `gold` | 10 victories and at least 1 loss |
| `silver` | 7–9 victories |
| `bronze` | 4–6 victories |
| misfortune | 0–3 victories; derived |

The additive invariants are:

```text
0 <= scored <= completed
ten_win = perfect + gold
misfortune = scored - perfect - gold - silver - bronze
```

`ten_win_days.known_count` counts Ten-Win Runs with a known final Run day;
`ten_win_days.sum_days` sums those days. Their ratio remains correct after
partitions are combined, and `known_count <= ten_win`.

### Matchups

A Matchup includes a Battle only when the opponent hero is canonical and the
winner resolves to `player` or `opponent`. For every emitted row:

```text
decided = wins + losses
matchup_win_rate = wins / decided
```

### Consumer calculations

| Display value | Calculation |
| --- | --- |
| Ten-Win rate | `ten_win / completed` |
| Completed Runs | `completed` |
| Run share | hero `completed / sum(completed)` in the same scope |
| Ten-Win Runs | `ten_win` |
| Average Ten-Win final day | `sum_days / known_count` |
| Perfect rate | `perfect / scored` |
| Gold rate | `gold / scored` |
| Silver rate | `silver / scored` |
| Bronze rate | `bronze / scored` |
| Misfortune rate | `misfortune / scored` |
| Matchup win rate | `wins / decided` |
| Seven-day trend | daily `ten_win / completed` |

A zero denominator produces `null`.

## Build corpus

`builds/latest.json` is calculated from all Accepted Runs in the Analysis
Window without rank segmentation. Its positional rows are self-described by
the payload's `schemas` object. Build IDs are zero-based positions in each
hero's `builds` array.

The global card and enchantment tables are uniquely and deterministically
sorted. Enchantment reference 0 means no enchantment. For each hero,
`card_index` maps every card reference to exactly the emitted Build IDs whose
identities contain that card.

### Eligible final layouts

A completed Accepted Run contributes a layout when:

- exactly one Battle is marked final and its ID equals the Run's
  `final_battle_id`;
- the final player-hand item snapshot is present and captured;
- every item has a valid card template ID, positive size, and in-range slot;
- the items occupy all 10 board slots exactly once.

Socket-effect overlays (`card_type = 7`) share a socket with an item. They are
removed before identity and occupancy are evaluated.

A Build Identity is its hero plus the sorted multiset of card template IDs.
Position, tier, and enchantment belong to layouts, not identity. Every eligible
run with that identity contributes to `completed_run_count`; every such Run
with 10 victories and non-negative losses contributes to
`ten_win_run_count`. An identity becomes a candidate after at least one
Ten-Win Run.

The Representative Layout is the most frequent exact layout among the
candidate's Ten-Win Runs. Frequency ties use canonical layout order: slot,
card template ID, tier, enchantment, and size, followed by lexical canonical
JSON order.

`ten_win_rate_bps` is the observed Ten-Win rate rounded to the nearest whole
basis point. `p75_ten_win_final_day` uses nearest rank over known Ten-Win final
days:

```text
sorted_days[ceil(0.75 * count) - 1]
```

It is `null` when no Ten-Win Run has a known final day.

### Score and selection

`score` is the 95% Wilson lower bound of the observed Ten-Win rate, scaled to
an integer:

```text
n = completed_run_count
k = ten_win_run_count
p = k / n
z = 1.96

lower = (p + z²/(2n) - z*sqrt(p*(1-p)/n + z²/(4n²)))
        / (1 + z²/n)
score = round(lower * 1,000,000)
```

For each hero, candidates are ordered by:

1. descending `score`;
2. descending `ten_win_run_count`;
3. ascending `p75_ten_win_final_day`, with `null` last;
4. ascending Build Identity.

The core corpus is the first 500 candidates. For every candidate card absent
from that core, append the highest-ranked candidate containing it. Appended
candidates retain the same ranking order and may cover several absent cards.

## Validation and publication

Each product passes its JSON Schema and semantic validation before its local
snapshot or public object is replaced. Product failures are isolated: a valid
snapshot can advance while the other key remains unchanged.

Public replacement uses canonical JSON and these headers:

```text
Cache-Control: public,max-age=60,must-revalidate
Content-Type: application/json
```

Consumers use `window.end` as data freshness.
