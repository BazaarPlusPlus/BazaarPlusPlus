export default `uniform float _EnchantmentDirectionX;
uniform float _EnchantmentDirectionY;
uniform vec4 _MainTex_ST;
uniform sampler2D _Turbo_Lightning;
uniform sampler2D _CardHighlight;
vec3 effect() {
  vec2 direction = vec2(_EnchantmentDirectionX, _EnchantmentDirectionY);
  float mask = texture(_EnchantmentMask, uv).g;
  float nearMask = texture(_EnchantmentMask, uv + direction).g;
  float farMask = texture(_EnchantmentMask, uv + 2.0 * direction).g;
  float outside = background(mask), range = _GlowMaskMax - _GlowMaskMin;
  float nearGlow =
      clamp((1.0 - (nearMask - _GlowMaskMin) / range) * outside + 1.0 - background(nearMask),
            0.0, 1.0);
  float farGlow = clamp(
      (1.0 - (farMask - _GlowMaskMax) / range) * outside + 1.0 - background(farMask), 0.0, 1.0);
  float edge = smoothstep(0.3, 1.0, outside * 0.29 * (nearGlow + glow(mask) + farGlow));
  vec3 mint = vec3(0.2022517, 0.9528302, 0.5551357);
  vec3 base = mix(texture(_MainTex, uv).rgb, mint,
                  edge * max(0.0, 10.0 * sin(edge - 1.1 * seconds) - 9.0));
  vec2 ray = polar((uv.yx - 0.5) * 2.0);
  vec2 lightningUV = ray * vec2(0.05, 2) + center() + seconds * vec2(-0.125, 0.1);
  base += mint * smoothstep(0.6, 1.0, ray.x) * texture(_Turbo_Lightning, lightningUV).r;
  vec2 distortion = texture(_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw).rg;
  ray = polar(flarePosition() + 0.5 * distortion * vec2(aspect, 1));
  vec3 highlight = texture(_CardHighlight, ray * vec2(0.6, 1) + vec2(seconds * 0.23, 0)).rgb *
                   (1.0 - outside);
  return base + shiftHue(highlight, 0.37058818, 0.0);
}`;
