import * as ui from '@bazaarplusplus/ui';
import * as game from '@bazaarplusplus/ui/game';
import { test, expect } from 'vite-plus/test';
import { pages } from '../src/pages';
const families: Record<string, string[]> = {
  button: ['Button'],
  badge: ['Badge'],
  dialog: [
    'Dialog',
    'DialogBody',
    'DialogCard',
    'DialogFooter',
    'DialogHeader'
  ],
  'page-header': ['PageHeader'],
  'segmented-control': ['SegmentedControl'],
  'status-banner': ['StatusBanner'],
  'status-panel': ['StatusPanel'],
  icons: [
    'AlertIcon',
    'CloseIcon',
    'CompassIcon',
    'ErrorIcon',
    'InboxIcon',
    'InfoIcon',
    'SpinnerIcon',
    'SuccessIcon',
    'WarningIcon'
  ],
  cards: ['CardTile', 'CardChip', 'SkillTile'],
  board: ['Board', 'SkillStrip'],
  'card-detail': ['CardDetail', 'CardText'],
  'day-card': ['DayCard'],
  'run-replay': ['RunReplay', 'MomentPanel'],
  'frame-inspector': ['FrameInspector']
};
test('each public visual export belongs to a live demo', () => {
  expect(Object.values(families).flat().sort()).toEqual(
    [...Object.keys(ui), ...Object.keys(game)]
      .filter(
        (k) =>
          ![
            'buttonClassName',
            'enchantments',
            'cardTiers',
            'gameTerms',
            'fillTerm',
            // The card catalog binds the components above to product data; the
            // UI kit's catalog tests and product Run Detail tests cover them.
            'CardCatalog',
            'createCardCatalogClient',
            'useCardCatalog',
            'DecisionReplay',
            'StreamFrames'
          ].includes(k)
      )
      .sort()
  );
  const demos = import.meta.glob('../src/demos/*.tsx');
  for (const slug of Object.keys(families)) {
    expect(pages.some(([key]) => key === slug)).toBe(true);
    expect(demos).toHaveProperty('../src/demos/' + slug + '.tsx');
  }
});
