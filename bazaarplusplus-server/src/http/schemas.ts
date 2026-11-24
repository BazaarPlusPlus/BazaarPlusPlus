import { z } from 'zod';
import { BUNDLE_ID, IDENTIFIER } from '../bundle/manifest';
import { BUNDLE_CONTENT_TYPE } from '../bundle/prefix';
import { API_ERROR_CODES } from '../errors';
import {
  GHOST_DEFAULT_LIMIT,
  GHOST_MAX_LIMIT,
  MAX_BUNDLE_BYTES,
  SYNC_DEFAULT_LIMIT,
  SYNC_MAX_LIMIT
} from '../limits';

// Query schemas describe the wire shape for OpenAPI. The handwritten parsers own error semantics.

const unixMs = z
  .int()
  .nonnegative()
  .meta({ description: 'Unix time in milliseconds' });
const bundleId = z.string().regex(BUNDLE_ID).meta({
  description: 'Canonical uppercase ULID',
  example: '01J00000000000000000000001'
});
const identifier = z
  .string()
  .regex(IDENTIFIER)
  .meta({ description: 'Opaque identity' });
const sha256Hex = z
  .string()
  .regex(/^[0-9a-f]{64}$/)
  .meta({ description: 'Lowercase hex SHA-256' });
const download = {
  download_url: z.url().meta({
    description:
      'Seven-day R2 presigned GET URL; a bearer capability for the complete Bundle'
  }),
  download_expires_at_ms: unixMs
};

export const HealthResponse = z
  .strictObject({ status: z.literal('ok'), server_time_ms: unixMs })
  .meta({ id: 'HealthResponse' });

export const BundleReceipt = z
  .strictObject({
    bundle_id: bundleId,
    run_id: identifier,
    outcome: z.enum(['stored', 'duplicate']).meta({
      description:
        '`stored` on the first logical commit (201), `duplicate` for identical bytes (200)'
    })
  })
  .meta({ id: 'BundleReceipt' });
export type BundleReceipt = z.infer<typeof BundleReceipt>;

export const BundleCollectionItem = z
  .strictObject({ bundle_id: bundleId, available_at_ms: unixMs, ...download })
  .meta({ id: 'BundleCollectionItem' });
export type BundleCollectionItem = z.infer<typeof BundleCollectionItem>;

export const BazaarDbBundleItem = z
  .strictObject({
    ...BundleCollectionItem.shape,
    run_id: identifier,
    content_type: z.literal(BUNDLE_CONTENT_TYPE),
    sha256: sha256Hex.meta({ description: 'Digest of the complete Bundle' })
  })
  .meta({ id: 'BazaarDbBundleItem' });
export type BazaarDbBundleItem = z.infer<typeof BazaarDbBundleItem>;

const CollectionWindow = z
  .strictObject({ available_from_ms: unixMs, available_before_ms: unixMs })
  .meta({ id: 'CollectionWindow' });
const CollectionCursor = z
  .strictObject({ available_at_ms: unixMs, bundle_id: bundleId })
  .nullable()
  .meta({ id: 'CollectionCursor', description: '`null` on the final page' });

function collectionPage<Item extends z.ZodType>(item: Item, id: string) {
  return z
    .strictObject({
      window: CollectionWindow,
      items: z.array(item).meta({
        description: 'Sorted by `available_at_ms ASC, bundle_id ASC`'
      }),
      next_after: CollectionCursor
    })
    .meta({ id });
}

export const BundleCollectionPage = collectionPage(
  BundleCollectionItem,
  'BundleCollectionPage'
);
export const BazaarDbBundlePage = collectionPage(
  BazaarDbBundleItem,
  'BazaarDbBundlePage'
);
export interface CollectionPage<Item> {
  window: z.infer<typeof CollectionWindow>;
  items: Item[];
  next_after: z.infer<typeof CollectionCursor>;
}

export const GhostBattleSummary = z
  .strictObject({
    battle_id: identifier,
    bundle_id: bundleId,
    recorded_at_ms: unixMs,
    day: z.int(),
    hour: z.int(),
    result: z.string(),
    winner_combatant_id: z.string(),
    is_final_battle: z.boolean(),
    player: z
      .strictObject({
        account_id: identifier,
        display_name: z.string(),
        hero_name: z.string().nullable(),
        rank: z.string().nullable(),
        rating: z.number().nullable()
      })
      .meta({ description: 'The uploader, who becomes the challenger' }),
    opponent: z
      .strictObject({
        account_id: identifier,
        hero_name: z.string().nullable(),
        level: z.int().nullable(),
        prestige: z.int().nullable(),
        victories: z.int().nullable()
      })
      .meta({
        description:
          'The queried local player, with the progress the challenger fought; null on rows stored before the progress columns'
      }),
    ...download
  })
  .meta({ id: 'GhostBattleSummary' });
export type GhostBattleSummary = z.infer<typeof GhostBattleSummary>;

export const GhostDiscoveryResponse = z
  .strictObject({
    battles: z
      .array(GhostBattleSummary)
      .meta({ description: 'Sorted by `recorded_at_ms DESC, battle_id DESC`' })
  })
  .meta({ id: 'GhostDiscoveryResponse' });
export type GhostDiscoveryResponse = z.infer<typeof GhostDiscoveryResponse>;

export const ApiErrorResponse = z
  .strictObject({
    error: z.strictObject({
      code: z.enum(API_ERROR_CODES),
      message: z
        .string()
        .meta({ description: 'Diagnostic text; clients do not match it' }),
      retryable: z.boolean(),
      request_id: z.string(),
      details: z.record(z.string(), z.unknown()).optional()
    })
  })
  .meta({ id: 'ApiErrorResponse' });

const queryInteger = z.int().nonnegative();

export const CollectionQuery = z.strictObject({
  available_from_ms: queryInteger.meta({
    description:
      'Inclusive window start; no earlier than server time minus 8 days'
  }),
  available_before_ms: queryInteger.optional().meta({
    description:
      'Exclusive window end; defaults to and may not exceed `server_now_ms - 60000`'
  }),
  limit: z
    .int()
    .min(1)
    .max(SYNC_MAX_LIMIT)
    .optional()
    .meta({ default: SYNC_DEFAULT_LIMIT }),
  after_available_at_ms: queryInteger.optional().meta({
    description:
      'Keyset cursor, paired with `after_bundle_id`: the last returned availability time'
  }),
  after_bundle_id: bundleId.optional().meta({
    description:
      'Keyset cursor, paired with `after_available_at_ms`: the last returned Bundle ID'
  })
});

export const GhostQuery = z.strictObject({
  player_account_id: identifier.meta({
    description: 'The local player whose Ghosts to find'
  }),
  limit: z
    .int()
    .min(1)
    .max(GHOST_MAX_LIMIT)
    .optional()
    .meta({ default: GHOST_DEFAULT_LIMIT })
});

export const IngestHeaders = z.object({
  'Content-Length': z.int().min(1).max(MAX_BUNDLE_BYTES).meta({
    description: 'Exact Bundle byte length'
  }),
  'Content-Digest': z.string().meta({
    description: 'Exactly one RFC 9530 value: `sha-256=:<base64 SHA-256>:`'
  })
});

export const BotPlayersResponse = z
  .strictObject({
    players: z.array(
      z.strictObject({ account_id: identifier, display_name: z.string() })
    ),
    has_more: z.boolean()
  })
  .meta({ id: 'BotPlayersResponse' });
export type BotPlayersResponse = z.infer<typeof BotPlayersResponse>;
export const BotPlayersQuery = z.strictObject({
  name: z.string().min(1).max(128)
});
export const BotBundlesResponse = z
  .strictObject({
    window: z.strictObject({
      stored_from_ms: unixMs,
      stored_before_ms: unixMs
    }),
    items: z.array(
      z.strictObject({
        bundle_id: bundleId,
        run_id: identifier,
        stored_at_ms: unixMs,
        sha256: sha256Hex,
        ...download
      })
    ),
    next_after: z
      .strictObject({ stored_at_ms: unixMs, bundle_id: bundleId })
      .nullable()
  })
  .meta({ id: 'BotBundlesResponse' });
export type BotBundlesResponse = z.infer<typeof BotBundlesResponse>;
export const BotBundlesQuery = z.strictObject({
  account_id: identifier,
  stored_from_ms: queryInteger.optional().meta({
    description:
      'Inclusive candidate window start, default Beijing midnight two days ago'
  }),
  stored_before_ms: queryInteger.optional().meta({
    description:
      'Exclusive candidate window end, default server now. Repeat the returned window on every page.'
  }),
  after_stored_at_ms: queryInteger.optional(),
  after_bundle_id: bundleId.optional()
});

export const AccountBindStartRequest = z
  .strictObject({
    email: z.string(),
    game_token: z.string(),
    game_account_id: identifier,
    platform: z.string(),
    flavor: z.string()
  })
  .meta({ id: 'AccountBindStartRequest' });

export const AccountBindStartResponse = z
  .strictObject({
    pending_id: z.string(),
    user_name: z.string(),
    email_masked: z.string(),
    expires_at_ms: unixMs
  })
  .meta({ id: 'AccountBindStartResponse' });
export type AccountBindStartResponse = z.infer<typeof AccountBindStartResponse>;

export const AccountBindConfirmRequest = z
  .strictObject({
    pending_id: z.string(),
    code: z.string().regex(/^[0-9]{6}$/)
  })
  .meta({ id: 'AccountBindConfirmRequest' });

export const AccountBindConfirmResponse = z
  .strictObject({
    device_token: z.string(),
    user_name: z.string(),
    email_masked: z.string()
  })
  .meta({ id: 'AccountBindConfirmResponse' });
export type AccountBindConfirmResponse = z.infer<
  typeof AccountBindConfirmResponse
>;

export const AccountDevicesRequest = z
  .strictObject({
    game_token: z.string(),
    game_account_id: identifier,
    platform: z.string(),
    flavor: z.string()
  })
  .meta({ id: 'AccountDevicesRequest' });

export const AccountDevicesResponse = z
  .strictObject({
    device_token: z.string(),
    user_name: z.string(),
    email_masked: z.string()
  })
  .meta({ id: 'AccountDevicesResponse' });
export type AccountDevicesResponse = z.infer<typeof AccountDevicesResponse>;

export const AccountMeResponse = z
  .strictObject({
    user_name: z.string(),
    email_masked: z.string()
  })
  .meta({ id: 'AccountMeResponse' });
export type AccountMeResponse = z.infer<typeof AccountMeResponse>;
