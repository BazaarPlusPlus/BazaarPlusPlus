export default `uniform float _ObsidianBrightness1;
uniform sampler2D _FierySparks;
uniform sampler2D _Mossy_OuterFlare;
uniform sampler2D _ObsidianTexture;
vec3 effect() {
  vec2 mask = texture(_EnchantmentMask, uv).gb;
  vec3 art = texture(_MainTex, uv).rgb;
  vec3 stone =
      texture(_ObsidianTexture,
              (uv * 4.0 + _AbsorptionMaskMax * vec2(65, 91)) * vec2(0.5, 1) + vec2(0.25, 0))
          .rgb;
  vec3 grey = mix(art, vec3(dot(art, vec3(0.299, 0.587, 0.114))), 0.75);
  vec3 base = mix(art, mix(grey, vec3(1), _ObsidianBrightness1), 0.65 * mask.y);
  vec3 overlay = mix(2.0 * base * stone, 1.0 - 2.0 * (1.0 - base) * (1.0 - stone),
                     greaterThan(stone, vec3(0.5)));
  base = clamp(mix(base, overlay, 0.65 * mask.y), 0.0, 1.0);
  base = clamp(mix(base, 1.0 - (1.0 - base) * vec3(1.574074, 1.902985, 1.583851), 0.2 * mask.y),
               0.0, 1.0);
  vec2 flareUV = polar(flarePosition()) * vec2(1.4, 1) + vec2(seconds * 0.2, 0);
  vec3 flare = texture(_Mossy_OuterFlare, flareUV).rgb * 0.85;
  vec3 steps = clamp((flare.r - vec3(0.5023881, 0.7955902, 0.9529259)) *
                         vec3(3.4105003, 6.355433, 21.23860),
                     0.0, 1.0);
  vec3 tint = vec3(0.172956, 0, 0.2075472) +
              min(1.0, flare.r / 0.5023881) * vec3(-0.0125786, 0, -0.1026851);
  tint = mix(tint, vec3(0.2358491, 0.0115991, 0), steps.x);
  tint = mix(tint, vec3(0.3396226, 0.0405838, 0.1539448), steps.y);
  tint = mix(tint, vec3(0.5, 0.1823899, 0.2287564), steps.z);
  float outside = background(mask.x);
  base = mix(base, tint * (1.0 + flare), outside * flare);
  vec2 sparkUV = polar(uv - 0.5 + center()) * vec2(1.3, 3) + vec2(seconds * 0.15, 0);
  float spark = texture(_FierySparks, sparkUV).r;
  vec3 sparkColor = min(1.0, 2.34023 * spark) * vec3(0.1303516, 0.0326925, 0.2735849);
  sparkColor = mix(sparkColor, vec3(0.1037736, 0, 0.0079826),
                   clamp((spark - 0.4272984) * 1.815995, 0.0, 1.0));
  sparkColor =
      mix(sparkColor, vec3(0.2358491, 0, 0.0191229), max(0.0, (spark - 0.9779507) * 45.332336));
  return mix(base, outside * sparkColor, outside * spark) + outside * sparkColor;
}`;
