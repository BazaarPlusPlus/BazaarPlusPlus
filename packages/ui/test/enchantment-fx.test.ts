import { describe, expect, it } from 'vite-plus/test';
import type { Enchantment, FxMaterial } from '../src/game/index';
import { fragment } from '../src/game/enchantment-fx/program';

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
    ['Deadly', 'DANGEROUS'],
    ['Ethereal', 'SHINY'],
    ['Fiery', 'FIERY'],
    ['Golden', 'GOLDEN'],
    ['Heavy', 'HEAVY'],
    ['Icy', 'ICY'],
    ['Mossy', 'MOSSY'],
    ['Mystical', 'MYSTICAL'],
    ['Obsidian', 'OBSIDIAN'],
    ['Radiant', 'PRISMATIC'],
    ['Restorative', 'RESTORATIVE'],
    ['Shielded', 'TEMPERED'],
    ['Shiny', 'DAZZLING'],
    ['Toxic', 'TOXIC'],
    ['Turbo', 'TURBO']
  ] as const)('renders %s across card sizes', async (enchant, keyword) => {
    const sources = await Promise.all(
      (['small', 'medium', 'large'] as const).map((size) =>
        fragment(material(enchant, keyword, size), enchant, false)
      )
    );
    for (const source of sources) expect(source).toContain('vec3 effect()');
  });

  it('allows Tiny only on Medium and Large cards in tiny mode', async () => {
    expect(
      await fragment(material('Tiny', 'TINY', 'small'), 'Tiny', true)
    ).toBeNull();
    expect(
      await fragment(material('Tiny', 'TINY', 'small'), 'Tiny', false)
    ).toBeNull();
    expect(
      await fragment(material('Tiny', 'TINY', 'medium'), 'Tiny', false)
    ).toBeNull();
    expect(
      await fragment(material('Tiny', 'TINY', 'large'), 'Tiny', false)
    ).toBeNull();
    expect(
      await fragment(material('Tiny', 'TINY', 'medium'), 'Tiny', true)
    ).toContain('_TinyBlocks');
    expect(
      await fragment(material('Tiny', 'TINY', 'large'), 'Tiny', true)
    ).toContain('_TinyBlocks');
  });

  it('checks the material revision and rejects tiny mode on non-Tiny enchantments', async () => {
    const atlas = material('Shiny', 'DAZZLING', 'medium');
    expect(await fragment(atlas, 'Shiny', false)).toContain(
      '_DazzlingGlintMap'
    );
    expect(await fragment(atlas, 'Shiny', true)).toBeNull();
    expect(await fragment({ ...atlas, version: 2 }, 'Shiny', false)).toBeNull();
  });
});
