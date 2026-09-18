const FORBIDDEN_FIELDS = new Set([
  "account_id",
  "player_account_id",
  "uploader_account_id",
  "opponent_account_id",
  "authorization",
  "token",
  "secret",
  "password",
  "body",
  "download_url",
  "presigned_url",
  "url",
  "projection_json",
  "screenshot",
  "redacted_fields",
]);

const FORBIDDEN_SUFFIXES = ["_account_id", "_token", "_secret", "_url"];

function sanitizeFields(fields: Record<string, unknown>): Record<string, unknown> {
  const sanitized: Record<string, unknown> = Object.create(null);
  const redactedFields: string[] = [];
  let keys: string[];

  try {
    keys = Object.keys(fields);
  } catch {
    return sanitized;
  }

  for (const key of keys) {
    let value: unknown;
    try {
      value = fields[key];
    } catch {
      redactedFields.push(key);
      continue;
    }

    const normalizedKey = key.toLowerCase();
    const forbiddenKey =
      FORBIDDEN_FIELDS.has(normalizedKey) ||
      FORBIDDEN_SUFFIXES.some((suffix) => normalizedKey.endsWith(suffix));
    const containsPresignedCredential = typeof value === "string" && value.includes("X-Amz-");

    let serializable = true;
    try {
      JSON.stringify(value);
    } catch {
      serializable = false;
    }

    if (forbiddenKey || containsPresignedCredential || !serializable) {
      redactedFields.push(key);
      continue;
    }

    sanitized[key] = value;
  }

  if (redactedFields.length > 0) {
    sanitized.redacted_fields = redactedFields;
  }

  return sanitized;
}

function writeLog(
  sink: (line: string) => void,
  event: string,
  fields: Record<string, unknown>,
): void {
  try {
    sink(JSON.stringify({ event, ...sanitizeFields(fields) }));
  } catch {
    // Observability must never change request handling.
  }
}

export function logEvent(event: string, fields: Record<string, unknown>): void {
  writeLog((line) => console.log(line), event, fields);
}

export function logError(event: string, fields: Record<string, unknown>): void {
  writeLog((line) => console.error(line), event, fields);
}
