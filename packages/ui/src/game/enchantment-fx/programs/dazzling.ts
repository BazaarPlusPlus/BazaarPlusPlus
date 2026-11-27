export default `uniform sampler2D _DazzlingGlintMap, _DazzlingStar2;
uniform float _ShimmerScale, _ShimmerUOffset, _ShimmerVOffset;
vec3 effect() {
  vec2 mask = texture(_EnchantmentMask, uv).gb;
  float pulse = 0.5 * (sin(seconds) + 1.0);
  float highlight = smoothstep(pulse, 1.0, absorption(mask.x) + glow(mask.x));
  vec2 position = (uv + _AbsorptionMaskMax * vec2(65, 91) - 0.5) * vec2(aspect, 1);
  vec3 glint = texture(_DazzlingGlintMap,
                       position * _ShimmerScale + vec2(_ShimmerUOffset, _ShimmerVOffset) + 0.5)
                   .rgb;
  float flash = min(1.0, glint.b * 100.0);
#ifndef SHIMMER_DEBUG
  flash *= max(0.0, sin(seconds * 4.0 + glint.b * 10.0));
#endif
  vec3 tint = vec3(1, 0.942697406, 0.742138326);
  return texture(_MainTex, uv).rgb +
         tint * (flash * texture(_DazzlingStar2, glint.rg).rgb + mask.y * pulse * 0.3) +
         highlight * vec3(0.15, 0.15, 0.10707546);
}`;
