import { integer, sqliteTable, text } from "drizzle-orm/sqlite-core";

// Read-only column mapping for Bundle identity queries. SQL migrations own the
// complete table definition, constraints, and indexes.
export const bundleIdentity = sqliteTable("bundles", {
  bundle_id: text("bundle_id").primaryKey(),
  run_id: text("run_id").notNull(),
  bundle_sha256: text("bundle_sha256").notNull(),
  has_screenshot: integer("has_screenshot").notNull(),
});
