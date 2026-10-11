import { apiError } from "../errors";

export function oneQueryValue(
  params: URLSearchParams,
  name: string,
  required: boolean,
): string | null {
  const values = params.getAll(name);
  if (values.length > 1 || (required && values.length === 0)) {
    throw apiError("invalid_query", `${name} must appear exactly once`, {
      details: {
        field: name,
      },
    });
  }
  return values[0] ?? null;
}
