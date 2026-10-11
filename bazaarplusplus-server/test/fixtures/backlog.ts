// Seeds `count` Bundles stored and available at one instant. Even-numbered
// Bundles carry a Screenshot, so the BazaarDB feed sees half of the backlog.
export async function seedBundleBacklog(
  db: D1Database,
  count: number,
  storedAtMs: number,
): Promise<void> {
  await db
    .prepare(`
    WITH RECURSIVE seq(n) AS (VALUES(0) UNION ALL SELECT n + 1 FROM seq WHERE n < ?1)
    INSERT INTO bundles (
      bundle_id, run_id, uploader_account_id, object_key, bundle_sha256,
      bundle_version, manifest_bytes, object_bytes, client_created_at_ms,
      stored_at_ms, available_at_ms, run_format_version, run_bytes, run_sha256,
      has_screenshot, screenshot_content_type, screenshot_bytes, screenshot_sha256
    )
    SELECT '01J9' || printf('%022d', n), 'backlog-' || n, 'backlog-uploader',
      'bundles/2026-08-02/01J9' || printf('%022d', n) || '.bundle',
      printf('%064d', 0), 6, 10, 100, ?2, ?2, ?2, 6, 10, printf('%064d', 0),
      CASE n % 2 WHEN 0 THEN 1 ELSE 0 END,
      CASE n % 2 WHEN 0 THEN 'image/jpeg' END,
      CASE n % 2 WHEN 0 THEN 10 END,
      CASE n % 2 WHEN 0 THEN printf('%064d', 0) END
    FROM seq
  `)
    .bind(count - 1, storedAtMs)
    .run();
}
