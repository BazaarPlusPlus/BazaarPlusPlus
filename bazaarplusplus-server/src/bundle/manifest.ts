const BUNDLE_ID = /^[0-7][0-9A-HJKMNP-TV-Z]{25}$/;
const IDENTIFIER = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;

export interface ValidatedBattleProjection {
  battle_id: string;
  recorded_at_ms: number;
  day: number;
  hour: number;
  encounter_id: string | null;
  combat_kind: string;
  result: string;
  winner_combatant_id: string | null;
  loser_combatant_id: string | null;
  is_final_battle: boolean;
  player: CombatantProjection;
  opponent: CombatantProjection;
}

export interface CombatantProjection {
  account_id: string;
  display_name: string;
  hero_id: string | null;
  hero_name: string | null;
  rank: string | null;
  rating: number | null;
  level: number | null;
  prestige: number | null;
  victories: number | null;
}

export interface ValidatedBundleDescriptor {
  bundleId: string;
  runId: string;
  uploaderAccountId: string;
  createdAtMs: number;
  manifestBytes: number;
  objectBytes: number;
  objectKey: string;
  run: { offset: number; length: number; sha256: string };
  screenshot: null | {
    offset: number;
    length: number;
    sha256: string;
    contentType: "image/jpeg" | "image/webp";
  };
  battles: ValidatedBattleProjection[];
}

export function validObjectKey(key: string): boolean {
  return /^bundles\/\d{4}-\d{2}-\d{2}\/[0-7][0-9A-HJKMNP-TV-Z]{25}\.bundle$/.test(key);
}

export function validBundleId(value: string): boolean {
  return BUNDLE_ID.test(value);
}

export function validIdentifier(value: string): boolean {
  return IDENTIFIER.test(value);
}

export function validAccountId(value: string): boolean {
  return validIdentifier(value);
}
