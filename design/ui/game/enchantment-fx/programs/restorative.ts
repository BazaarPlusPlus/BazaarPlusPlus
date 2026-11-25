export default `uniform float _RandomnessWiggle;
uniform sampler2D _Shared_Smoke;
uniform sampler2D _DazzlingGlintMap;
uniform sampler2D _HeartTexture;
uniform sampler2D _NewSmoke;
uniform sampler2D _Mossy_OuterFlare;
vec3 effect() {
  vec2 p = polar(uv - 0.5) * vec2(1.4, 1);
  vec3 smoke = texture(_Shared_Smoke, p - seconds * 0.15).rgb *
               texture(_Shared_Smoke, p * vec2(2, 1) - seconds * 0.15).rgb;
  vec2 mask = texture(_EnchantmentMask, uv).rg;
  float outside = background(mask.g);
  vec3 base = mix(texture(_MainTex, uv).rgb, vec3(0.1647059, 0.8509804, 0.2165116),
                  smoke * 1.5 * (absorption(mask.g) + glow(mask.g)));
  vec3 mist = texture(_NewSmoke, p - seconds * 0.075).rgb;
  vec3 pulse = texture(_Shared_Smoke, p - seconds * 0.075).rgb * mist * mask.r * outside *
               0.75 * (sin(seconds * 3.0) + 1.0);
  base = mix(base, vec3(0.1611486, 0.9150943, 0.4045109), pulse);
  vec2 seed = uv * 0.9 + _AbsorptionMaskMax * vec2(39, 91) + _RandomnessWiggle;
  vec3 glint = textureLod(_DazzlingGlintMap, seed * vec2(0.5, 1) + vec2(0.25, 0), 0.0).rgb;
  float flash = max(0.0, (sin(seconds * 0.8 + glint.b * 10.0) - 0.4) / 0.6) *
                min(1.0, glint.b * 100.0) * texture(_HeartTexture, glint.rg).r;
  vec3 heart = flash * vec3(0, 0.764151, 0.0944456);
  base = mix(base, heart, 0.45 * flash) + heart;
  vec2 flareUV = polar(flarePosition()) * vec2(1, 2) - vec2(seconds * 0.2, 0);
  vec3 flare = smoothstep(vec3(0.2), vec3(2), texture(_Mossy_OuterFlare, flareUV).rgb);
  return base + flare * mist * outside * vec3(0.8295194, 1.5, 0.5896226);
}`;
