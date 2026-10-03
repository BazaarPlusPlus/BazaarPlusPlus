import { Validator } from "@cfworker/json-schema";
import { expect, test } from "vitest";
import manifest from "../../contracts/v5/fixtures/run-only.manifest.json";
import schema from "../../contracts/v5/manifest.schema.json";

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
