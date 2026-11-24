import type { ValidatedBundleDescriptor } from '../bundle/manifest';
import { BUNDLE_PREFIX_BYTES, RUN_CONTENT_TYPE } from '../bundle/prefix';
import { apiError } from '../errors';

// A linked BPP Account's own Runs keep their Run segment in R2 beyond the
// 8-day Bundle lifecycle, which covers only `bundles/` (ADR 0007). The server
// copies the bytes and never decodes them.

const R2_DELETE_BATCH = 1000;

function accountPrefix(accountId: string): string {
  return `account-runs/${accountId}/`;
}

export function runDetailKey(accountId: string, runId: string): string {
  return `${accountPrefix(accountId)}${runId}.run`;
}

export async function copyRunDetail(
  bucket: R2Bucket,
  descriptor: ValidatedBundleDescriptor,
  accountId: string
): Promise<void> {
  const { offset, length } = descriptor.run;
  try {
    const object = await bucket.get(descriptor.objectKey, {
      range: {
        offset: BUNDLE_PREFIX_BYTES + descriptor.manifestBytes + offset,
        length
      }
    });
    if (object === null) throw new Error('Bundle object is missing');
    // A ranged object's `size` is the whole object, so count the bytes read.
    const bytes = await object.arrayBuffer();
    if (bytes.byteLength !== length) throw new Error('Run range is short');
    await bucket.put(runDetailKey(accountId, descriptor.runId), bytes, {
      httpMetadata: { contentType: RUN_CONTENT_TYPE }
    });
  } catch {
    throw apiError('storage_unavailable', 'Run detail copy failed');
  }
}

// Deletes the copy this upload wrote when no Run Summary of the same BPP Account
// points at it: the account was unlinked or deleted while the upload ran, after
// its R2 prefix had already been cleared. Best effort; a failed check keeps the
// copy, since it may be the detail of a stored summary.
export async function dropUnclaimedRunDetail(
  bucket: R2Bucket,
  db: D1Database,
  accountId: string,
  runId: string
): Promise<boolean> {
  try {
    const claimed = await db
      .prepare(
        `SELECT 1 AS claimed
         FROM run_summaries AS s
         JOIN bpp_accounts AS a ON a.game_account_id = s.game_account_id
         WHERE s.run_id = ?1 AND a.account_id = ?2 AND s.detail_stored = 1`
      )
      .bind(runId, accountId)
      .first();
    if (claimed !== null) return false;
    await bucket.delete(runDetailKey(accountId, runId));
    return true;
  } catch {
    return false;
  }
}

export async function readRunDetail(
  bucket: R2Bucket,
  accountId: string,
  runId: string
): Promise<R2ObjectBody | null> {
  try {
    return await bucket.get(runDetailKey(accountId, runId));
  } catch {
    throw apiError('storage_unavailable', 'Run detail storage is unavailable');
  }
}

export async function deleteAccountRunDetails(
  bucket: R2Bucket,
  accountId: string
): Promise<void> {
  const prefix = accountPrefix(accountId);
  try {
    // Each pass deletes what it listed, so the next list starts over.
    while (true) {
      const listed = await bucket.list({ prefix, limit: R2_DELETE_BATCH });
      if (listed.objects.length === 0) return;
      await bucket.delete(listed.objects.map(({ key }) => key));
      if (!listed.truncated) return;
    }
  } catch {
    throw apiError('storage_unavailable', 'Run detail delete failed');
  }
}
