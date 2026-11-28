export default `uniform float _RandomnessWiggle;
uniform sampler2D _Shared_Smoke;
uniform sampler2D _DazzlingGlintMap;
uniform sampler2D _HeartTexture;
uniform sampler2D _NewSmoke;
uniform sampler2D _Mossy_OuterFlare;
vec3 effect() {
  vec2 p = polar((uv - 0.5) * vec2(aspect, 1.0)) * vec2(1.4, 1);
  vec3 smoke = texture(_Shared_Smoke, p - seconds * 0.15).rgb *
               texture(_Shared_Smoke, p * vec2(2, 1) - seconds * 0.15).rgb;
  vec2 mask = texture(_EnchantmentMask, uv).rg;
  float outside = background(mask.g);
  float rim = absorption(mask.g) + glow(mask.g);
  vec3 base = mix(texture(_MainTex, uv).rgb, vec3(0.165, 0.86, 0.23),
                  clamp(smoke * 1.55 * rim, 0.0, 0.80));
  vec3 mist = texture(_NewSmoke, p - seconds * 0.075).rgb;
  vec3 pulse = texture(_Shared_Smoke, p - seconds * 0.075).rgb * mist * mask.r * outside *
               (0.30 + 0.55 * (sin(seconds * 2.6) + 1.0));
  base = mix(base, vec3(0.18, 0.92, 0.42), clamp(pulse, 0.0, 0.82));
  vec2 seed = uv * 0.9 + _AbsorptionMaskMax * vec2(39, 91) + _RandomnessWiggle;
  vec3 glint = textureLod(_DazzlingGlintMap, cardUV(seed), 0.0).rgb;
  float flash = max(0.0, (sin(seconds * 1.0 + glint.b * 10.0) - 0.25) / 0.75) *
                min(1.0, glint.b * 100.0) * texture(_HeartTexture, glint.rg).r;
  vec3 heart = flash * vec3(0.10, 0.94, 0.24);
  base = mix(base, heart, 0.55 * flash) + heart * 1.55;
  vec2 flareUV = polar(flarePosition()) * vec2(1, 2) - vec2(seconds * 0.2, 0);
  vec3 flare = smoothstep(vec3(0.18), vec3(1.75), texture(_Mossy_OuterFlare, flareUV).rgb);
  return base + flare * mist * outside * vec3(0.80, 1.55, 0.60);
}`;
