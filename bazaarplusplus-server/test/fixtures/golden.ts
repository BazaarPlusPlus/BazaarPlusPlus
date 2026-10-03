import { expect } from "vitest";

// Compares one worker.fetch response with test/goldens/<name>.json.
// Regenerate with `npx vitest run -u` (or BPP_UPDATE_GOLDENS=1 npm test).
// Callers live one directory below test/, so the path is relative to their file.
export async function expectGolden(response: Response, name: string): Promise<void> {
  const golden = { status: response.status, body: await response.json() };
  await expect(`${JSON.stringify(golden, null, 2)}\n`).toMatchFileSnapshot(
    `../goldens/${name}.json`,
  );
}
