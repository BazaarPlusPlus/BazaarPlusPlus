import { Validator } from "@cfworker/json-schema";
import { expect, test } from "vitest";
import manifest from "../../contracts/v6/fixtures/run-only.manifest.json";
import schema from "../../contracts/v6/manifest.schema.json";
import { makeBundleFixture } from "../fixtures/bundle";

// The interpreting validator runs inside workerd, which forbids ajv's generated code.
const validator = new Validator(schema as object, "2020-12");

test("the canonical Run-only manifest satisfies the published manifest schema", () => {
  const result = validator.validate(manifest);
  expect(result.errors).toEqual([]);
  expect(result.valid).toBe(true);
});

test("the manifest schema rejects a manifest missing a required field", () => {
  const { run_id: _omitted, ...run } = manifest.run;
  expect(validator.validate({ ...manifest, run }).valid).toBe(false);
});

test("the manifest schema requires non-null winner and loser combatant ids", async () => {
  const { manifest: withBattle } = await makeBundleFixture();
  expect(validator.validate(withBattle).valid).toBe(true);
  const run = withBattle.run as { projection: { battles: Record<string, unknown>[] } };
  for (const field of ["winner_combatant_id", "loser_combatant_id"]) {
    const battle = { ...run.projection.battles[0], [field]: null };
    const invalid = {
      ...withBattle,
      run: { ...run, projection: { ...run.projection, battles: [battle] } },
    };
    expect(validator.validate(invalid).valid).toBe(false);
  }
});
