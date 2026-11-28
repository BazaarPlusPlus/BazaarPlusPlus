import { cellular } from './cellular';

export default `${cellular}
uniform sampler2D _TemperedCrystal, _OffsetMask, _TriangleTex;
uniform vec4 _OffsetMask_ST;
uniform float _OutlineTempered;
vec3 effect() {
  float mask = texture(_EnchantmentMask, uv).g;
  float back = background(mask);
  vec3 art = texture(_MainTex, uv).rgb;
  // The reference fragment binds a clamped sampler to the scrolling crystal lookup.
  vec2 texel = 0.5 / vec2(textureSize(_TemperedCrystal, 0));
  vec2 crystalUV = cardUV(uv) * vec2(1.35, 2.7) + vec2(0.0, 0.025 * seconds);
  float crystal = texture(_TemperedCrystal, clamp(crystalUV, texel, 1.0 - texel)).r;
  float pulse = max(0.0, sin((0.5 * uv.x + crystal) * 3.0 + 1.4 * seconds));
  vec3 gold = vec3(1.0, 0.76, 0.18);
  // Crisp specular glint band only — no flat yellow foreground wash.
  art += (1.0 - back) * gold * (0.20 * pow(pulse * crystal, 1.6));
  art += gold * (glow(mask) * _OutlineTempered * (0.40 + 0.85 * cellularLight()) + 0.32 * absorption(mask));
  vec3 offset = texture(_OffsetMask, uv * _OffsetMask_ST.xy + _OffsetMask_ST.zw).rgb;
  vec2 p = rotateUV(12.5 * (uv - offset.rg), 3.0 * (offset.b - 0.5) * seconds);
  float width = 10.0 * sin(dot(offset, vec3(10)) + 0.60 * seconds) - 7.8;
  vec3 triangle = textureLod(_TriangleTex, vec2(p.x / max(abs(width), 0.001), p.y) + 0.5, 0.0).rgb;
  width = max(width, 0.0) * 0.72;
  vec3 edge = triangle.g * vec3(1.05, 0.78, 0.46) * width * width;
  edge += (triangle.r * width + triangle.b * width * width) * vec3(0.62, 0.42, 0.18);
  return art + back * edge * 1.65;
}`;
