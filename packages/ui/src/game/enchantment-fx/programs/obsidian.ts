export default `uniform float _ObsidianBrightness1;
uniform sampler2D _FierySparks;
uniform sampler2D _Mossy_OuterFlare;
uniform sampler2D _ObsidianTexture;
vec3 effect() {
  vec2 mask = texture(_EnchantmentMask, uv).gb;
  vec3 art = texture(_MainTex, uv).rgb;
  float outside = background(mask.x);
  vec3 stone =
      texture(_ObsidianTexture,
              cardUV(uv * 3.8 + _AbsorptionMaskMax * vec2(65, 91)))
          .rgb;
  // Apply obsidian glass/stone strictly on the authored mask.y surface (no blanket tinting).
  vec3 grey = mix(art, vec3(dot(art, vec3(0.299, 0.587, 0.114))), 0.78);
  vec3 base = mix(art, mix(grey, vec3(1.0), _ObsidianBrightness1), 0.65 * mask.y);
  vec3 overlay = mix(2.0 * base * stone, 1.0 - 2.0 * (1.0 - base) * (1.0 - stone),
                     greaterThan(stone, vec3(0.5)));
  base = clamp(mix(base, overlay, 0.65 * mask.y), 0.0, 1.0);
  base = clamp(mix(base, 1.0 - (1.0 - base) * vec3(1.574074, 1.902985, 1.583851), 0.2 * mask.y),
               0.0, 1.0);
  // Subtle background shadow so dark-violet & ruby wisps and embers pop clearly.
  base = mix(base, base * vec3(0.55, 0.48, 0.62), outside * 0.26);
  vec2 flareUV = polar(flarePosition()) * vec2(1.4, 1) + vec2(seconds * 0.2, 0);
  vec3 flare = texture(_Mossy_OuterFlare, flareUV).rgb * 1.08;
  vec3 steps = clamp((flare.r - vec3(0.42, 0.72, 0.90)) *
                         vec3(3.4, 6.2, 16.0),
                     0.0, 1.0);
  vec3 tint = vec3(0.18, 0.008, 0.25) + min(1.0, flare.r * 2.0) * vec3(0.05, 0.0, 0.08);
  tint = mix(tint, vec3(0.38, 0.02, 0.12), steps.x);
  tint = mix(tint, vec3(0.60, 0.06, 0.24), steps.y);
  tint = mix(tint, vec3(0.88, 0.20, 0.34), steps.z);
  base = mix(base, tint * (1.1 + flare), clamp(outside * flare * 1.15, 0.0, 0.88));
  vec2 sparkUV = polar((uv - 0.5 + center()) * vec2(aspect, 1.0)) * vec2(1.3, 3) + vec2(seconds * 0.16, 0);
  float spark = texture(_FierySparks, sparkUV).r;
  vec3 sparkColor = min(1.0, 2.4 * spark) * vec3(0.34, 0.05, 0.62);
  sparkColor = mix(sparkColor, vec3(0.68, 0.05, 0.16),
                   clamp((spark - 0.38) * 2.1, 0.0, 1.0));
  sparkColor =
      mix(sparkColor, vec3(0.96, 0.24, 0.32), max(0.0, (spark - 0.80) * 6.0));
  return mix(base, sparkColor, clamp(outside * spark * 1.15, 0.0, 0.88)) + outside * sparkColor * 0.65;
}`;
