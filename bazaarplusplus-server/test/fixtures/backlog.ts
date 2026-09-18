export async function seedDeliveryBacklog(
  db: D1Database,
  count: number,
  storedAtMs: number,
  claimableAtMs = storedAtMs,
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
      printf('%064d', 0), 5, 10, 100, ?2, ?2, ?2, 5, 10, printf('%064d', 0),
      1, 'image/jpeg', 10, printf('%064d', 0) FROM seq
  `)
    .bind(count - 1, storedAtMs)
    .run();
  await db
    .prepare(`
    INSERT INTO bazaardb_deliveries (bundle_id, claimable_at_ms, created_at_ms, state_updated_at_ms)
    SELECT bundle_id, ?1, stored_at_ms, stored_at_ms FROM bundles
    WHERE uploader_account_id = 'backlog-uploader'
  `)
    .bind(claimableAtMs)
    .run();
}
