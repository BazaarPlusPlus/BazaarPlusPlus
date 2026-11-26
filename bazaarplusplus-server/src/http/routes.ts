import { z } from 'zod';
import { BUNDLE_CONTENT_TYPE } from '../bundle/prefix';
import { MAX_BUNDLE_BYTES } from '../limits';
import { bindGameAccount } from '../modules/account-service';
import {
  collectBazaarDbBundles,
  collectBundles
} from '../modules/bundle-collection';
import { collectBotBundles, findBotPlayers } from '../modules/bot-queries';
import { ingestBundle } from '../modules/bundle-ingest';
import { discoverGhostBattles } from '../modules/ghost-battle-discovery';
import { buildOpenApiDocument, scalarPage } from './openapi';
import type { RouteDefinition } from './route-shell';
import {
  AccountBindRequest,
  AccountBindResponse,
  BotPlayersQuery,
  BotPlayersResponse,
  BotBundlesQuery,
  BotBundlesResponse,
  BazaarDbBundlePage,
  BundleCollectionPage,
  BundleReceipt,
  CollectionQuery,
  GhostDiscoveryResponse,
  GhostQuery,
  HealthResponse,
  IngestHeaders
} from './schemas';

const COLLECTION_ERRORS = [
  'invalid_query',
  'window_not_settled',
  'unauthorized',
  'insufficient_scope',
  'window_expired',
  'storage_unavailable'
] as const;

let openApiDocument: object | undefined;

export const ROUTES = [
  {
    path: '/bot/players',
    method: 'GET',
    auth: 'bot_stats',
    handler: async (context) => ({
      status: 200,
      body: await findBotPlayers(context.request, context.env)
    }),
    docs: {
      tags: ['Bot'],
      summary: 'Find recent uploaders by exact game name',
      requestParams: { query: BotPlayersQuery },
      responses: {
        '200': {
          description:
            'Recent named players; Ghost projections do not cover every uploader',
          content: { 'application/json': { schema: BotPlayersResponse } }
        }
      },
      errors: [
        'invalid_query',
        'unauthorized',
        'insufficient_scope',
        'storage_unavailable'
      ]
    }
  },
  {
    path: '/bot/bundles',
    method: 'GET',
    auth: 'bot_stats',
    handler: async (context) => ({
      status: 200,
      body: await collectBotBundles(context.request, context.env)
    }),
    docs: {
      tags: ['Bot'],
      summary: 'List recent Bundle candidates for one account',
      requestParams: { query: BotBundlesQuery },
      responses: {
        '200': {
          description:
            'A fixed window, keyset-paged in stored time; filter decoded Run end times in the consumer',
          content: { 'application/json': { schema: BotBundlesResponse } }
        }
      },
      errors: [
        'invalid_query',
        'unauthorized',
        'insufficient_scope',
        'storage_unavailable'
      ]
    }
  },

  {
    path: '/health',
    method: 'GET',
    handler: async () => ({
      status: 200,
      body: { status: 'ok', server_time_ms: Date.now() }
    }),
    docs: {
      tags: ['Health'],
      summary: 'Liveness probe',
      description: 'Touches no D1, R2, secret, limiter, or presigner.',
      responses: {
        '200': {
          description: 'Alive',
          content: { 'application/json': { schema: HealthResponse } }
        }
      },
      errors: []
    }
  },
  {
    path: '/bundles',
    method: 'GET',
    auth: 'bundle_sync',
    handler: async (context) => ({
      status: 200,
      body: await collectBundles(
        context.request,
        context.env,
        context.requestId
      )
    }),
    docs: {
      tags: ['Discovery'],
      summary: 'Enumerate Bundles for the analyzer',
      description:
        'Keyset pages over a fixed, stateless availability window. Parameters may appear only ' +
        'once and unknown parameters are invalid. The Worker signs only returned rows.',
      requestParams: { query: CollectionQuery },
      responses: {
        '200': {
          description: 'One page of the window',
          content: { 'application/json': { schema: BundleCollectionPage } }
        }
      },
      errors: COLLECTION_ERRORS
    }
  },
  {
    path: '/bundles',
    method: 'POST',
    handler: async (context) => {
      const result = await ingestBundle(
        context.request,
        context.env,
        context.requestId
      );
      return { status: result.status, body: result.receipt };
    },
    docs: {
      tags: ['Ingest'],
      summary: 'Upload one sealed Bundle',
      description:
        'Public and unauthenticated: `run.player_account_id` is a caller assertion. Per-client ' +
        'rate limited before any header or body is read. The body ' +
        'is the Bundle V6 binary (prefix, manifest, Run, optional Screenshot), streamed into R2 ' +
        'while length and digests are validated. Stable `invalid_bundle.details.reason` values ' +
        'are listed in `docs/api-reference.md`.',
      requestParams: { header: IngestHeaders },
      requestBody: {
        required: true,
        content: {
          [BUNDLE_CONTENT_TYPE]: {
            schema: z.string().meta({
              override: {
                type: 'string',
                contentMediaType: BUNDLE_CONTENT_TYPE
              },
              description: `Bundle V6 binary, 1–${MAX_BUNDLE_BYTES} bytes`
            })
          }
        }
      },
      responses: {
        '201': {
          description: 'First logical commit',
          content: { 'application/json': { schema: BundleReceipt } }
        },
        '200': {
          description: 'Identical Bundle already stored',
          content: { 'application/json': { schema: BundleReceipt } }
        }
      },
      errors: [
        'invalid_content_length',
        'invalid_content_digest',
        'bundle_id_conflict',
        'run_already_bundled',
        'content_length_required',
        'bundle_too_large',
        'unsupported_content_type',
        'invalid_bundle',
        'unsupported_bundle_version',
        'unsupported_run_format',
        'bundle_digest_mismatch',
        'segment_digest_mismatch',
        'run_without_battles',
        'rate_limited',
        'storage_unavailable'
      ]
    }
  },
  {
    path: '/ghost-battles',
    method: 'GET',
    handler: async (context) => ({
      status: 200,
      body: await discoverGhostBattles(
        context.request,
        context.env,
        context.requestId
      )
    }),
    docs: {
      tags: ['Discovery'],
      summary: 'Discover Ghost Battles against a player',
      description:
        'Per-client rate limited before parsing. Looks back exactly five days. An unknown account ' +
        'returns an empty list.',
      requestParams: { query: GhostQuery },
      responses: {
        '200': {
          description: 'Ghost Battle summaries',
          content: { 'application/json': { schema: GhostDiscoveryResponse } }
        }
      },
      errors: ['invalid_query', 'rate_limited', 'storage_unavailable']
    }
  },
  {
    path: '/bazaardb/bundles',
    method: 'GET',
    auth: 'bazaardb_delivery',
    handler: async (context) => ({
      status: 200,
      body: await collectBazaarDbBundles(
        context.request,
        context.env,
        context.requestId
      )
    }),
    docs: {
      tags: ['Discovery'],
      summary: 'BazaarDB Feed of Screenshot-bearing Bundles',
      description:
        '`GET /bundles` with a Screenshot filter: same query, settle point, lookback, keyset ' +
        'order, and cursor. The consumer stores its own cursor.',
      requestParams: { query: CollectionQuery },
      responses: {
        '200': {
          description: 'One page of the window',
          content: { 'application/json': { schema: BazaarDbBundlePage } }
        }
      },
      errors: COLLECTION_ERRORS
    }
  },
  {
    path: '/account/bind',
    method: 'POST',
    handler: async (context) => ({
      status: 200,
      body: await bindGameAccount(context.request, context.env)
    }),
    docs: {
      tags: ['Account'],
      summary: 'Link a game account to a BPP Account with a Bind Code',
      description:
        'The player creates the BPP Account and its Bind Code on bazaarplusplus.com. The Worker ' +
        'verifies the official game token against the read-only official account summary ' +
        'endpoint and links the game account in one D1 batch that consumes the code. A game ' +
        'account linked to another BPP Account leaves the code unused.',
      requestBody: {
        required: true,
        content: {
          'application/json': { schema: AccountBindRequest }
        }
      },
      responses: {
        '200': {
          description: 'Game account linked',
          content: { 'application/json': { schema: AccountBindResponse } }
        }
      },
      errors: [
        'invalid_body',
        'invalid_bind_code',
        'game_token_rejected',
        'account_mismatch',
        'game_account_already_bound',
        'rate_limited',
        'game_api_unavailable',
        'storage_unavailable'
      ]
    }
  },
  {
    path: '/openapi.json',
    method: 'GET',
    handler: async () => {
      openApiDocument ??= buildOpenApiDocument(ROUTES);
      return { status: 200, body: openApiDocument };
    },
    docs: {
      tags: ['Documentation'],
      summary: 'OpenAPI 3.1 document',
      description: 'Generated from the route table and its Zod schemas.',
      responses: { '200': { description: 'This document' } },
      errors: []
    }
  },
  {
    path: '/docs',
    method: 'GET',
    handler: async () => ({ status: 200, html: scalarPage('/openapi.json') }),
    docs: {
      tags: ['Documentation'],
      summary: 'Interactive API reference',
      description: 'A Scalar page rendering `/openapi.json`.',
      responses: {
        '200': { description: 'HTML page', content: { 'text/html': {} } }
      },
      errors: []
    }
  }
] as const satisfies readonly RouteDefinition[];
