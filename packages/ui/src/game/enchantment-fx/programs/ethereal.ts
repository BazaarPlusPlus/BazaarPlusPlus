import { cellular, sparks } from './cellular';

export default `${cellular}
${sparks}
uniform sampler2D _ChaoticFlowmap, _ShinyNoise, _Shared_Smoke;
uniform float _ShinyGlintHeight, _ShinyNoiseTile, _Size;
uniform float _ShinyFlowScale, _FlowStrenght, _FlowSpeed;
vec3 effect() {
  float mask = texture(_EnchantmentMask, uv).g;
  float back = background(mask);
  float rim = absorption(mask);
  float halo = glow(mask);
  float light = cellularLight();
  vec3 art = texture(_MainTex, uv).rgb;
  // Two-phase flow-advected iridescent silk from _ShinyNoise strictly in the aura and rim.
  vec2 flowSampleUV = (uv - 0.5) * vec2(aspect, 1.0) * _ShinyFlowScale + center() + 0.5;
  vec2 flow = texture(_ChaoticFlowmap, flowSampleUV).gr + uv;
  vec2 velocity = flow * _FlowStrenght * (1.0 - 2.0 * flow);
  vec2 noiseBase = (uv * _ShinyNoiseTile + _AbsorptionMaskMax * 13.0) / max(_Size, 0.001);
  float phase = fract(seconds * _FlowSpeed);
  vec3 silkA = texture(_ShinyNoise, noiseBase + velocity * phase).rgb;
  vec3 silkB = texture(_ShinyNoise, noiseBase + velocity * fract(phase + 0.5)).rgb;
  vec3 silk = mix(silkA, silkB, abs(2.0 * phase - 1.0));
  vec3 shifted = shiftHue(silk, light * 0.35 + seconds * 0.08, light * 0.42);
  vec3 spectral = mix(shifted, vec3(0.32, 0.84, 1.0), clamp(rim + halo, 0.0, 1.0) * 0.42);
  // Swirling ethereal mist around the silhouette and background (no flat foreground veil).
  vec3 smoke = texture(_Shared_Smoke, uv * 2.0 - seconds * 0.15).rgb
             * texture(_Shared_Smoke, uv - seconds * 0.15).rgb;
  vec3 mistWeight = clamp(vec3(back * mask * 0.95) + 1.85 * smoke * (rim + halo), 0.0, 0.65);
  art = mix(art, spectral * 0.96, mistWeight);
  // Crisp flow-warped star glints dancing across the aura.
  vec2 warp = textureLod(_ChaoticFlowmap, (uv - 0.5) * vec2(aspect, 1.0) * 0.25 + center() + 0.5, 0.0).gr;
  vec2 starUV = cardUV(uv) + warp * 0.35 + seconds * vec2(0.025, -0.03);
  float rays = sparks(starUV * 6.5, 0.017, true);
  float dots = sparks((starUV + vec2(0.17, 0.31)) * 4.8, 0.011, false);
  float heightFade = mix(0.35, 1.0, smoothstep(_ShinyGlintHeight * 0.6, 1.0, uv.y));
  art += (0.20 + 0.80 * back) * heightFade
       * (rays * vec3(0.72, 0.96, 1.0) * 1.18 + dots * vec3(0.45, 0.90, 1.0) * 0.92);
  // Specular cellular edge catch on the silhouette rim only.
  art += light * vec3(0.24, 0.78, 1.0) * 0.24 * (rim + halo);
  return art;
}`;
