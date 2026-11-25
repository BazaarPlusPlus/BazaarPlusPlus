import { gzipSync } from 'node:zlib';

import { encodeMessagePack } from '../../../runs/test/fixtures/encode';
export { encodeMessagePack };
export function gzipRun(root: unknown[]): Uint8Array {
  return new Uint8Array(gzipSync(encodeMessagePack(root)));
}

type Tier = 'Bronze' | 'Silver' | 'Gold' | 'Diamond' | 'Legendary';

/** [instance id, template id, name, size] for a card that keeps its identity across Battles. */
type CardSpec = readonly [string, string, string, 1 | 2 | 3];

const ITEMS = {
  belle: ['i-belle', '362f4a5d-0085-4270-9666-87afa213df20', 'ISO-Belle', 1],
  tambourine: [
    'i-tambourine',
    'd6c55b70-fcc7-454c-af9c-06b99d2d01e3',
    'Tambourine',
    1
  ],
  bangles: ['i-bangles', 'fea41e28-c739-43eb-b517-de8ac096a297', 'Bangles', 1],
  microphone: [
    'i-microphone',
    'a176e286-6309-4652-ab61-afcb7fbfa7de',
    'Microphone',
    1
  ],
  trumpet: ['i-trumpet', '794c2d7e-f6b0-45d5-ae2c-7e31899d962e', 'Trumpet', 1],
  saxophone: [
    'i-saxophone',
    '9d33944f-745c-41aa-b282-66b9bd7250b7',
    'Saxophone',
    2
  ],
  flute: ['i-flute', '9ccd5d5d-8527-4106-94dd-90be201c37b2', 'Flute', 1],
  wings: ['i-wings', '3d05c2c8-05e5-4569-a9fd-7f351035b201', 'Wings', 2],
  tuba: ['i-tuba', '93001232-e995-43a9-82e0-bd6615ea746e', 'Tuba', 2],
  keyboard: [
    'i-keyboard',
    '5f96d1ec-f10b-49ba-b264-26090fdfe0b9',
    'Mini Keyboard',
    1
  ],
  bass: ['i-bass', '04312f35-0ac9-4fd0-9118-6367be0d4690', 'Bass', 2],
  patch: ['i-patch', 'd32d8efd-ca3c-49d5-b9e3-dc9a6d481ed9', 'Patch', 1],
  projector: [
    'i-projector',
    'f113f6c0-7c39-412f-b797-81e565de25cb',
    'Star Projector',
    1
  ]
} as const satisfies Record<string, CardSpec>;

const SKILLS = {
  keenEye: ['s-keen', 'b49dd22b-ef95-4f7e-954e-556e1e431d4a', 'Keen Eye', 2],
  appetites: [
    's-appetites',
    '19c967e5-f4c0-4c11-bb96-aa039369600a',
    'Large Appetites',
    2
  ],
  waves: [
    's-waves',
    'd2f910bc-3490-456f-8ab0-aedda752bc5c',
    'Crashing Waves',
    2
  ]
} as const satisfies Record<string, CardSpec>;

const ITEM_TYPE = 0;
const SKILL_TYPE = 2;
const SOCKET_EFFECT_TYPE = 7;

function card(
  [instanceId, templateId, name, size]: CardSpec,
  type: number,
  slot: number | null,
  tier: Tier,
  enchant: string | null = null
): unknown[] {
  return [
    instanceId,
    templateId,
    type,
    size,
    0,
    slot,
    name,
    tier,
    enchant,
    [],
    {}
  ];
}

type BattleSpec = {
  day: number;
  pvp?: boolean;
  win: boolean;
  opponent: string | null;
  final?: boolean;
  /** Null records the hand as Missing. */
  hand: Array<[CardSpec, number, Tier, string?]> | null;
  skills: Array<[CardSpec, Tier]>;
};

export const SAMPLE_RUN_ID = 'run-sample-0001';
const PLAYER_ID = 'player-sample';

const BATTLES: BattleSpec[] = [
  {
    day: 1,
    win: false,
    opponent: 'Pygmalien',
    hand: [
      [ITEMS.microphone, 2, 'Bronze'],
      [ITEMS.bangles, 3, 'Bronze'],
      [ITEMS.tambourine, 4, 'Silver', 'Shiny'],
      [ITEMS.trumpet, 6, 'Bronze'],
      [ITEMS.belle, 7, 'Silver']
    ],
    skills: []
  },
  {
    day: 2,
    pvp: false,
    win: true,
    opponent: null,
    hand: null,
    skills: [[SKILLS.keenEye, 'Bronze']]
  },
  {
    day: 3,
    win: true,
    opponent: 'Dooley',
    hand: [
      [ITEMS.flute, 0, 'Bronze'],
      [ITEMS.belle, 1, 'Silver'],
      [ITEMS.saxophone, 2, 'Bronze'],
      [ITEMS.bangles, 4, 'Silver'],
      [ITEMS.trumpet, 5, 'Silver'],
      [ITEMS.tambourine, 8, 'Silver', 'Shiny']
    ],
    skills: [[SKILLS.keenEye, 'Bronze']]
  },
  {
    day: 5,
    win: true,
    opponent: 'Mak',
    hand: [
      [ITEMS.patch, 0, 'Bronze'],
      [ITEMS.belle, 1, 'Silver'],
      [ITEMS.keyboard, 2, 'Silver'],
      [ITEMS.tuba, 3, 'Silver'],
      [ITEMS.wings, 5, 'Silver'],
      [ITEMS.bass, 7, 'Silver']
    ],
    skills: [
      [SKILLS.keenEye, 'Bronze'],
      [SKILLS.appetites, 'Silver']
    ]
  },
  {
    day: 8,
    win: true,
    opponent: 'TheDragons',
    hand: [
      [ITEMS.patch, 0, 'Gold'],
      [ITEMS.belle, 1, 'Gold'],
      [ITEMS.keyboard, 2, 'Silver'],
      [ITEMS.tuba, 3, 'Gold', 'Turbo'],
      [ITEMS.wings, 5, 'Silver'],
      [ITEMS.bass, 7, 'Diamond'],
      [ITEMS.projector, 9, 'Gold']
    ],
    skills: [
      [SKILLS.keenEye, 'Bronze'],
      [SKILLS.appetites, 'Silver'],
      [SKILLS.waves, 'Gold']
    ]
  },
  {
    day: 11,
    win: false,
    opponent: 'Vanessa',
    final: true,
    hand: [
      [ITEMS.patch, 0, 'Gold'],
      [ITEMS.belle, 1, 'Gold'],
      [ITEMS.keyboard, 2, 'Silver'],
      [ITEMS.tuba, 3, 'Diamond', 'Turbo'],
      [ITEMS.wings, 5, 'Silver'],
      [ITEMS.bass, 7, 'Diamond'],
      [ITEMS.projector, 9, 'Gold']
    ],
    skills: [
      [SKILLS.keenEye, 'Bronze'],
      [SKILLS.appetites, 'Silver'],
      [SKILLS.waves, 'Gold']
    ]
  }
];

const STARTED_AT_MS = Date.UTC(2026, 10, 20, 12, 0);

/** A `pve_combat_recorded` event body, snake_case as the mod stores it. */
function pve(day: number, winner: string, encounterId?: string): string {
  return JSON.stringify({
    day,
    hour: 6,
    encounter_id: encounterId,
    combat_kind: 'Combat',
    winner,
    loser: winner === 'Player' ? 'Opponent' : 'Player',
    frame_count: 400,
    duration_ms: 20_000
  });
}

function battle(spec: BattleSpec, index: number): unknown[] {
  const handSet = spec.hand
    ? [
        'player_hand',
        spec.hand.length ? 'Captured' : 'CapturedEmpty',
        'capture',
        [
          ...spec.hand.map(([c, slot, tier, enchant]) =>
            card(c, ITEM_TYPE, slot, tier, enchant ?? null)
          ),
          // The hand set also holds socket effects, which are not board items.
          card(
            ['e-socket', '04eca54a-69bf-4874-8b6d-56d284bb58be', 'Socket', 1],
            SOCKET_EFFECT_TYPE,
            6,
            'Gold'
          )
        ]
      ]
    : ['player_hand', 'Missing', 'capture', []];
  const skillSet = [
    'player_skills',
    spec.skills.length ? 'Captured' : 'CapturedEmpty',
    'capture',
    spec.skills.map(([c, tier]) => card(c, SKILL_TYPE, null, tier))
  ];
  const opponent = `opponent-${index}`;
  return [
    `battle-${index}`,
    [
      STARTED_AT_MS + index * 600_000,
      spec.day,
      6,
      null,
      // The mod records a captured monster fight as the game's Combat state.
      spec.pvp === false ? 'Combat' : 'PVPCombat',
      spec.win ? 'win' : 'loss',
      spec.win ? PLAYER_ID : opponent,
      spec.win ? opponent : PLAYER_ID,
      spec.final ?? false
    ],
    [
      [PLAYER_ID, null, 'Stelle', 'Gold', 1200, 10, 0, 0, 8, 10, 0, 0],
      [
        opponent,
        null,
        spec.opponent,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        0,
        0
      ]
    ],
    [[handSet, skillSet]],
    null
  ];
}

/** A Stelle Run with a Missing hand on day 2, tier-ups, a rebuild on day 5 and an enchant. */
export function sampleRunPayload(): Uint8Array {
  return gzipRun([
    6,
    SAMPLE_RUN_ID,
    PLAYER_ID,
    [
      'Stelle',
      'Ranked',
      null,
      STARTED_AT_MS,
      STARTED_AT_MS + 3_600_000,
      'completed',
      11,
      6,
      8,
      3,
      'Gold',
      1200,
      'Gold',
      1218,
      18,
      null,
      null,
      null,
      null,
      null,
      null,
      '6.5.0'
    ],
    [
      [1, STARTED_AT_MS, 'run_started', '{}'],
      // The same fight as the captured day-2 Battle: not shown twice.
      [2, STARTED_AT_MS + 600_000, 'pve_combat_recorded', pve(2, 'Player')],
      // Day 4: known only from its event.
      [
        3,
        STARTED_AT_MS + 1_500_000,
        'pve_combat_recorded',
        pve(4, 'Player', 'enc-tide-turner')
      ]
    ],
    BATTLES.map(battle),
    [],
    [[], [], 0, false],
    null
  ]);
}

/** Every TemplateId the sample Run shows, for serving Card Art. */
export const SAMPLE_TEMPLATE_IDS: string[] = [
  ...Object.values(ITEMS),
  ...Object.values(SKILLS)
].map(([, templateId]) => templateId);
