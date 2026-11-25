import { describe, expect, it } from 'vite-plus/test';
import type { Enchantment, FxMaterial } from '@bpp/ui';
import { fragment } from '../../../../design/ui/game/enchantment-fx/program';

function material(
  enchant: Enchantment,
  keyword: string,
  size: FxMaterial['size'] = 'small'
): FxMaterial {
  const normal = [
    '_CRDSIZE_' + size.toUpperCase(),
    '_DEBUG_OFF',
    '_ENCHANTMENTSTATUS_' + keyword
  ];
  return {
    version: 1,
    profile: 'premium-card-v1',
    size,
    values: {},
    textures: {},
    keywords: { [enchant]: { normal, tiny: [...normal, '_ISTINY_ON'] } }
  };
}

describe('PremiumCard program selection', () => {
  it.each([
    ['Ethereal', 'SHINY'],
    ['Golden', 'GOLDEN'],
    ['Heavy', 'HEAVY'],
    ['Icy', 'ICY'],
    ['Mossy', 'MOSSY'],
    ['Mystical', 'MYSTICAL'],
    ['Shielded', 'TEMPERED']
  ] as const)('uses the static frame for %s', async (enchant, keyword) => {
    expect(
      await fragment(material(enchant, keyword), enchant, false)
    ).toBeNull();
  });

  it('checks the material revision and the Tiny geometry independently of the enchantment', async () => {
    const atlas = material('Shiny', 'DAZZLING', 'medium');
    expect(await fragment(atlas, 'Shiny', false)).toContain(
      '_DazzlingGlintMap'
    );
    expect(await fragment(atlas, 'Shiny', true)).toBeNull();
    expect(await fragment({ ...atlas, version: 2 }, 'Shiny', false)).toBeNull();
    expect(await fragment(material('Tiny', 'TINY'), 'Tiny', true)).toContain(
      '_Shared_Smoke'
    );
  });
});
