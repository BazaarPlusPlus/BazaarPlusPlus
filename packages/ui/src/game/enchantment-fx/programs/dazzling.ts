import { cellular, sparks } from './cellular';

export default `${cellular}
${sparks}
uniform sampler2D _DazzlingGlintMap, _DazzlingStar2;
uniform float _ShimmerScale, _ShimmerUOffset, _ShimmerVOffset;
vec3 effect() {
  vec2 mask = texture(_EnchantmentMask, uv).gb;
  float back = background(mask.x);
  float rim = absorption(mask.x) + glow(mask.x);
  float pulse = 0.30 + 0.70 * (0.5 * (sin(seconds * 1.3) + 1.0));
  float highlight = smoothstep(0.55, 1.0, rim);
  vec2 position = (uv + _AbsorptionMaskMax * vec2(65, 91) - 0.5) * vec2(aspect, 1);
  vec3 glint = texture(_DazzlingGlintMap,
                       position * _ShimmerScale + vec2(_ShimmerUOffset, _ShimmerVOffset) + 0.5)
                   .rgb;
  float flash = min(1.0, glint.b * 100.0);
#ifndef SHIMMER_DEBUG
  flash *= 0.25 + 0.75 * pow(0.5 + 0.5 * sin(seconds * 3.8 + glint.b * 12.0), 2.0);
#endif
  vec3 tint = vec3(1.0, 0.96, 0.82);
  vec3 art = texture(_MainTex, uv).rgb;
  // Tight diagonal specular gleam without washing out the artwork.
  float sweepCoord = uv.x * 0.75 + uv.y * 0.55 - fract(seconds * 0.24) * 2.2 + 0.45;
  float sweep = exp(-sweepCoord * sweepCoord * 68.0) * (0.30 + 0.70 * (1.0 - back));
  // Crisp jewel-like 4-point diamond star flares.
  vec2 p = (uv - 0.5) * vec2(aspect, 1.0);
  float starA = sparks(rotateUV(p, seconds * 0.04) * 4.8 + vec2(0.2, 0.1), 0.015, true);
  float starB = sparks(rotateUV(p, -seconds * 0.03) * 7.2 + vec2(1.7, 2.3), 0.010, true);
  float twinkle = 0.45 + 0.55 * sin(seconds * 3.0 + uv.x * 6.0 - uv.y * 5.0);
  float light = cellularLight();
  vec3 sheen = tint * (
    flash * texture(_DazzlingStar2, glint.rg).rgb * 2.05
    + mask.y * 0.28 * pulse
    + sweep * 0.22
    + (starA * 1.15 + starB * 0.72) * twinkle * (0.60 + 0.40 * light)
  );
  return art + sheen + highlight * vec3(0.14, 0.13, 0.09);
}`;
