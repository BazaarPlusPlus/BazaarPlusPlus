# BazaarPlusPlus Analyzer

Use these terms for source collection, analysis, and consumer behavior.

## Source collection

**Bundle**:
An immutable delivery item containing one Run and its associated Battles and
card snapshots.

**Source Hour**:
The UTC hour assigned from a Bundle's server-observed availability time.

**Source Day**:
The UTC date composed of all 24 Source Hours for that date.

**Complete Source Day**:
A Source Day sealed from 24 verified Source Hours.

**Source Epoch**:
An optional inclusive UTC date before which Source Days are outside the data
population. Pre-epoch days are not healed, sealed, or analyzed.

**Analysis Window**:
The sequence ending at the latest sealed Complete Source Day and extending
backward through consecutive Complete Source Days, with a minimum of one day
and a maximum of seven days. It never crosses the Source Epoch.

## Runs and segments

**Accepted Run**:
A Run whose hero and final rank are recognized. A Run without a recognized
final rank, together with its associated Battles and card snapshots, is not
part of the analyzed population.

**Legend Segment**:
Accepted Runs whose canonical source value for final rank is `Legendary`.

**Non-Legend Segment**:
Accepted Runs whose final rank is recognized and is not Legend.

**All Segment**:
The union of the Legend Segment and Non-Legend Segment. It is derived by
addition rather than stored separately.

**Scored Run**:
A completed Accepted Run whose victory and loss values place it in a defined
outcome bucket.

**Ten-Win Run**:
A completed Accepted Run with ten victories, including both perfect and
non-perfect results.

**Ten-Win Rate**:
Ten-Win Runs divided by completed Accepted Runs in the same hero, segment,
and time scope.

## Battles and builds

**Matchup**:
The decided Battles played by one hero against another hero. A Matchup's
segment is owned by the player's Run, not by the opponent.

**Matchup Win Rate**:
Player-side Battle wins divided by decided Matchup Battles.

**Build Identity**:
A hero plus the sorted multiset of card template identifiers in a completed
final board. Card position, tier, and enchantment are not part of the identity.

**Representative Layout**:
The most frequently observed complete layout among a Build Identity's
Ten-Win Runs.

**Build Corpus**:
The current set of selected Build Identities and their Representative Layouts
used by the Mod for recommendation recall and ranking.

## Operations

**Operational Evidence**:
Local records of pipeline progress and outcomes used by operators to understand
the current Run, the last completed Run, and recent Run history. It is not
consumer data.
