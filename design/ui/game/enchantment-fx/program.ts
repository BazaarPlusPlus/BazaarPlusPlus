import type { Enchantment } from '../types';

export type FxTexture =
  | {
      size: number[];
      file: string;
      wrap: number[];
      filter: number;
      mipCount: number;
      colorSpace: number;
      st: number[];
    }
  | { default: string; file?: never; st: number[] };

export type FxMaterial = {
  version: number;
  profile: string;
  size: 'small' | 'medium' | 'large';
  keywords: Partial<Record<Enchantment, Record<'normal' | 'tiny', string[]>>>;
  values: Record<string, number | number[]>;
  textures: Record<string, FxTexture>;
};
export type CardFx = {
  material(): Promise<FxMaterial>;
  texture(filename: string): Promise<Blob>;
  tiny?: boolean;
  slots?: number;
};

export const vertex = `#version 300 es
out vec2 uv;
void main() {
  uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
  gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
}`;

export function hasProgram(enchant: Enchantment) {
  return programs[enchant] !== undefined;
}

export async function fragment(
  material: FxMaterial,
  enchant: Enchantment,
  tinyMode: boolean
) {
  if (material.version !== 1 || material.profile !== 'premium-card-v1')
    return null;
  const program = programs[enchant];
  if (
    !program ||
    (material.size !== 'small' && !program.allSizes) ||
    (tinyMode && enchant !== 'Tiny')
  )
    return null;

  const keywords = material.keywords[enchant]?.[tinyMode ? 'tiny' : 'normal'];
  const size = '_CRDSIZE_' + material.size.toUpperCase();
  const status = '_ENCHANTMENTSTATUS_' + program.keyword;
  if (
    !keywords?.includes(size) ||
    !keywords.includes(status) ||
    keywords.includes('_ISTINY_ON') !== tinyMode
  )
    return null;
  // The flow-map switches share the same non-premium fragment for these programs.
  const supported = new Set([
    size,
    status,
    '_DEBUG_OFF',
    '_MAINTEXASFLOWMAP_FLOAT243_ON',
    '_MAINTEXTUREFLOWMAP_FLOAT273_ON',
    '_ISTINY_ON'
  ]);
  if (enchant === 'Shiny') supported.add('_SHIMMERDEBUG_ON');
  if (keywords.some((keyword) => !supported.has(keyword))) return null;
  const math = (await program.module()).default;
  return `#version 300 es
${keywords.includes('_SHIMMERDEBUG_ON') ? '#define SHIMMER_DEBUG' : ''}
precision highp float;

in vec2 uv;
out vec4 result;
uniform float seconds, aspect, _ItemCutoutBias;
uniform sampler2D _MainTex, _EnchantmentMask;
uniform float _AbsorptionMaskMin, _AbsorptionMaskMax, _GlowMaskMin, _GlowMaskMax;
uniform float _EnchantmentUCenter, _EnchantmentVCenter, _EnchantmentFlareScale;
vec2 center() {
  return vec2(_EnchantmentUCenter, _EnchantmentVCenter);
}
vec2 flarePosition() {
  return ((uv - 0.5) * _EnchantmentFlareScale + center()) * vec2(aspect, 1);
}
vec2 polar(vec2 p) {
  return vec2(length(p), atan(p.x, p.y) / 6.28318531);
}
vec2 rotateUV(vec2 p, float angle) {
  float c = cos(angle), s = sin(angle);
  return mat2(c, -s, s, c) * p;
}
float background(float mask) {
  return smoothstep(0.0, 0.02, _ItemCutoutBias + 0.01 - mask);
}
float absorption(float mask) {
  return smoothstep(0.15, 0.90,
                    (1.0 - background(mask)) * (_AbsorptionMaskMax - _AbsorptionMaskMin) /
                        (mask - _AbsorptionMaskMin));
}
float glow(float mask) {
  return clamp(background(mask) * (1.0 - (mask - _GlowMaskMin) / (_GlowMaskMax - _GlowMaskMin)),
               0.0, 1.0);
}
vec3 shiftHue(vec3 rgb, float shift, float boost) {
  vec4 p = rgb.g >= rgb.b ? vec4(rgb.gb, 0, -1.0 / 3.0) : vec4(rgb.bg, -1, 2.0 / 3.0);
  vec4 q = rgb.r >= p.x ? vec4(rgb.r, p.yzx) : vec4(p.xyw, rgb.r);
  float range = q.x - min(q.w, q.y);
  float hue = abs(q.z + (q.w - q.y) / (6.0 * range + 1e-10)) + shift;
  vec3 ramp =
      clamp(abs(fract(hue + vec3(1, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0, 0.0, 1.0);
  return (q.x + boost) * mix(vec3(1), ramp, range / (q.x + 1e-10));
}
${math}
void main() {
  vec3 rgb = clamp(effect(), 0.0, 1.0);
  result = vec4(mix(12.92 * rgb, 1.055 * pow(rgb, vec3(1.0 / 2.4)) - 0.055,
                    greaterThan(rgb, vec3(0.0031308))),
                1);
}`;
}

const programs: Partial<
  Record<
    Enchantment,
    {
      keyword: string;
      allSizes?: boolean;
      module: () => Promise<{ default: string }>;
    }
  >
> = {
  Mystical: {
    keyword: 'MYSTICAL',
    module: () => import('./programs/mystical')
  },
  Mossy: { keyword: 'MOSSY', module: () => import('./programs/mossy') },
  Golden: { keyword: 'GOLDEN', module: () => import('./programs/golden') },
  Heavy: { keyword: 'HEAVY', module: () => import('./programs/heavy') },
  Shielded: {
    keyword: 'TEMPERED',
    module: () => import('./programs/tempered')
  },
  Shiny: {
    keyword: 'DAZZLING',
    allSizes: true,
    module: () => import('./programs/dazzling')
  },
  Tiny: { keyword: 'TINY', module: () => import('./programs/tiny') },
  Deadly: {
    keyword: 'DANGEROUS',
    allSizes: true,
    module: () => import('./programs/dangerous')
  },
  Fiery: { keyword: 'FIERY', module: () => import('./programs/fiery') },
  Obsidian: {
    keyword: 'OBSIDIAN',
    module: () => import('./programs/obsidian')
  },
  Radiant: {
    keyword: 'PRISMATIC',
    module: () => import('./programs/prismatic')
  },
  Restorative: {
    keyword: 'RESTORATIVE',
    module: () => import('./programs/restorative')
  },
  Toxic: { keyword: 'TOXIC', module: () => import('./programs/toxic') },
  Turbo: { keyword: 'TURBO', module: () => import('./programs/turbo') }
};
