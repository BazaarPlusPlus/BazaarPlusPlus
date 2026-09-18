import { vi } from "vitest";

export interface RecordedD1Query {
  sql: string;
  bindings: unknown[];
  result: D1Result;
}

// Observe all() and batch() at the D1 seam while executing native statements.
// The caller restores the spies with vi.restoreAllMocks() after each test.
export function recordD1(db: D1Database): RecordedD1Query[] {
  const queries: RecordedD1Query[] = [];
  const prepared = new WeakMap<D1PreparedStatement, Omit<RecordedD1Query, "result">>();
  const prepare = db.prepare.bind(db);
  const batch = db.batch.bind(db);

  function observe(statement: D1PreparedStatement, sql: string, bindings: unknown[]) {
    const query = { sql, bindings };
    prepared.set(statement, query);
    const bind = statement.bind.bind(statement);
    const all = statement.all.bind(statement);
    vi.spyOn(statement, "bind").mockImplementation((...values) =>
      observe(bind(...values), sql, values),
    );
    vi.spyOn(statement, "all").mockImplementation(async <T>() => {
      const result = await all<T>();
      queries.push({ ...query, result });
      return result;
    });
    return statement;
  }

  vi.spyOn(db, "prepare").mockImplementation((sql) => observe(prepare(sql), sql, []));
  vi.spyOn(db, "batch").mockImplementation(async <T>(statements: D1PreparedStatement[]) => {
    const results = await batch<T>(statements);
    statements.forEach((statement, index) => {
      const query = prepared.get(statement);
      if (query === undefined) throw new Error("D1 batch contains an unrecorded statement");
      queries.push({ ...query, result: results[index] });
    });
    return results;
  });
  return queries;
}
