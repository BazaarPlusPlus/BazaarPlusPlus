import { HttpError } from "../errors";

const MAX_JSON_BYTES = 65_536;

export function oneQueryValue(
  params: URLSearchParams,
  name: string,
  required: boolean,
): string | null {
  const values = params.getAll(name);
  if (values.length > 1 || (required && values.length === 0)) {
    throw new HttpError(400, "invalid_query", `${name} must appear exactly once`, false, {
      field: name,
    });
  }
  return values[0] ?? null;
}

export async function readJsonObject(request: Request): Promise<Record<string, unknown>> {
  if (request.headers.get("Content-Type") !== "application/json") {
    throw new HttpError(400, "invalid_json", "Content-Type must be application/json", false);
  }
  const declared = request.headers.get("Content-Length");
  if (
    declared !== null &&
    (/^[0-9]+$/.test(declared) === false || Number(declared) > MAX_JSON_BYTES)
  ) {
    throw new HttpError(400, "invalid_json", "JSON body exceeds 64 KiB", false);
  }
  if (request.body === null) {
    throw new HttpError(400, "invalid_json", "JSON body is required", false);
  }
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let bytes = 0;
  while (true) {
    const result = await reader.read();
    if (result.done) break;
    bytes += result.value.byteLength;
    if (bytes > MAX_JSON_BYTES) {
      await reader.cancel("JSON body too large");
      throw new HttpError(400, "invalid_json", "JSON body exceeds 64 KiB", false);
    }
    chunks.push(result.value);
  }
  const body = new Uint8Array(bytes);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  let value: unknown;
  try {
    const text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(body);
    value = JSON.parse(text);
  } catch {
    throw new HttpError(400, "invalid_json", "Request body is not valid JSON", false);
  }
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new HttpError(400, "invalid_json", "JSON root must be an object", false);
  }
  return value as Record<string, unknown>;
}
