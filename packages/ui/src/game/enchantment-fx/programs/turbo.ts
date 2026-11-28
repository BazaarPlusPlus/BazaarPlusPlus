export default `uniform float _EnchantmentDirectionX, _EnchantmentDirectionY;
uniform vec4 _MainTex_ST;
uniform sampler2D _Turbo_Lightning;
uniform sampler2D _CardHighlight;
vec3 effect() {
  vec2 direction = vec2(_EnchantmentDirectionX, _EnchantmentDirectionY);
  if (length(direction) < 0.002) direction = vec2(-0.035, -0.02);
  float mask = texture(_EnchantmentMask, uv).g;
  float nearMask = texture(_EnchantmentMask, uv + direction).g;
  float farMask = texture(_EnchantmentMask, uv + 2.0 * direction).g;
  float outside = background(mask);
  float rawDenom = _GlowMaskMax - _GlowMaskMin;
  float denom = abs(rawDenom) < 0.001 ? 0.001 : rawDenom;
  float nearGlow =
      clamp((1.0 - (nearMask - _GlowMaskMin) / denom) * outside + 1.0 - background(nearMask),
            0.0, 1.0);
  float farGlow = clamp(
      (1.0 - (farMask - _GlowMaskMax) / denom) * outside + 1.0 - background(farMask), 0.0, 1.0);
  float edge = smoothstep(0.24, 1.0, outside * 0.34 * (nearGlow + glow(mask) + farGlow));
  vec3 mint = vec3(0.20, 0.96, 0.58);
  float trailPulse = 0.30 + 0.70 * pow(0.5 + 0.5 * sin(edge * 4.2 - 2.4 * seconds), 2.0);
  vec3 base = mix(texture(_MainTex, uv).rgb, mint, clamp(edge * trailPulse * 0.92, 0.0, 0.80));
  vec2 ray = polar((uv.yx - 0.5) * 2.0);
  vec2 lightningUV = ray * vec2(0.055, 2.0) + center() + seconds * vec2(-0.18, 0.13);
  float bolts = texture(_Turbo_Lightning, lightningUV).r;
  float bolts2 = texture(_Turbo_Lightning, ray * vec2(0.09, 3.0) - center() + seconds * vec2(-0.24, -0.08)).r;
  base += mint * smoothstep(0.38, 0.95, ray.x) * (bolts * 1.45 + bolts2 * 0.75);
  vec2 distortion = texture(_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw).rg;
  ray = polar(flarePosition() + 0.5 * distortion * vec2(aspect, 1));
  vec3 highlight = texture(_CardHighlight, ray * vec2(0.6, 1) + vec2(seconds * 0.25, 0)).rgb *
                   (1.0 - outside);
  return base + shiftHue(highlight, 0.37058818, 0.0) * 1.30;
}`;
