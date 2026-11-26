import * as ui from '@bpp/ui';
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
  cards: ['CardTile', 'CardArt', 'CardChip', 'TierLabel', 'EnchantLabel'],
  board: ['Board', 'SkillStrip'],
  'card-detail': ['CardDetail', 'CardText'],
  'day-card': ['DayCard'],
  'run-replay': ['RunReplay', 'MomentPanel'],
  'frame-inspector': ['FrameInspector']
};
test('each public visual export belongs to a live demo', () => {
  expect(Object.values(families).flat().sort()).toEqual(
    Object.keys(ui)
      .filter(
        (k) => !['buttonClassName', 'enchantments', 'cardTiers'].includes(k)
      )
      .sort()
  );
  const demos = import.meta.glob('../src/demos/*.tsx');
  for (const slug of Object.keys(families)) {
    expect(pages.some(([key]) => key === slug)).toBe(true);
    expect(demos).toHaveProperty('../src/demos/' + slug + '.tsx');
  }
});
