import { cellular, runes } from './cellular';

export default `${cellular}
${runes}
uniform sampler2D _MysticalBorealLines;
uniform vec4 _MysticalHue;
vec3 effect() {
  vec2 mask = texture(_EnchantmentMask, uv).gb;
  vec3 art = texture(_MainTex, uv).rgb;
  float back = background(mask.x);
  float outline = glow(mask.x);
  float light = dot(art, vec3(0.299, 0.587, 0.114)) + cellularLight();
  // Restrict mystical tint to the authored mask.y surface only; keep card art un-dyed.
  art = mix(art, light * _MysticalHue.rgb, 0.42 * mask.y);
  vec2 linesUV = polar(flarePosition()) * vec2(0.6, 1);
  linesUV.x -= 0.12 * (seconds + 221.0 * _AbsorptionMaskMax);
  vec4 lines = texture(_MysticalBorealLines, linesUV);
  art = mix(art, lines.rgb * 1.35, clamp((back - outline * 0.6) * lines.a * 1.25, 0.0, 0.92));
  art = mix(art, art * _MysticalHue.rgb, outline);
  float rune = runeChannels(back).b;
  vec3 color = mix(vec3(0.06, 0.015, 0.26), vec3(0.18, 0.03, 0.42),
                   clamp(rune * 8.474, 0.0, 1.0));
  color = mix(color, vec3(0.72, 0.20, 1.0), clamp((rune - 0.117998) * 1.297732, 0.0, 1.0));
  color = mix(color, vec3(1.0, 0.48, 1.0), max(0.0, (rune - 0.85) * 8.5));
  return mix(art, art * min(color, vec3(1)), rune) + color * (rune * 1.48);
}`;
