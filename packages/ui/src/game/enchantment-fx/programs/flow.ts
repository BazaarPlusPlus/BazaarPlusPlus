export const flow = `uniform sampler2D _ChaoticFlowmap;
uniform vec4 _ChaoticFlowmap_ST;
uniform sampler2D _Shared_Smoke;
uniform sampler2D _FierySparks;
uniform float _FlowSpeed, _FlowStrenght, _Size, _FieryDistortion;
uniform float _ShinyGlintHeight;
struct Flame {
  float weight;
  float heat;
  float mask;
};
Flame flame() {
  vec2 flow = texture(_ChaoticFlowmap, uv * _ChaoticFlowmap_ST.xy + _ChaoticFlowmap_ST.zw).gr + uv;
  vec2 velocity = flow * _FlowStrenght * (1.0 - 2.0 * flow);
  float phase = fract(seconds * _FlowSpeed);
  vec2 noiseUV =
      textureLod(_ChaoticFlowmap, uv * _ChaoticFlowmap_ST.xy + _ChaoticFlowmap_ST.zw, 0.0).rg;
  vec2 noise = texture(_Shared_Smoke,
                       cardUV(noiseUV) + seconds * vec2(-0.15, 0.15))
                   .rg;
  float inner = texture(_EnchantmentMask, uv * 0.85).g;
  vec2 distorted = (uv + noise * _FieryDistortion * inner) / _Size;
  float first = texture(_EnchantmentMask, distorted + velocity * phase).r;
  float second = texture(_EnchantmentMask, distorted + velocity * fract(phase + 0.5)).r;
  float mask = texture(_EnchantmentMask, uv).g, outside = background(mask);
  float weight = clamp(mix(first, second, abs(2.0 * phase - 1.0)) * outside * 1.20, 0.0, 1.0);
  vec2 sampleUV = uv + center();
  float coverage = 0.3333 * (texture(_EnchantmentMask, sampleUV).r +
                             texture(_EnchantmentMask, 0.8 * sampleUV).r +
                             texture(_EnchantmentMask, 0.9 * sampleUV).r);
  float smoke =
      texture(_Shared_Smoke, cardUV(uv) * 1.5 - seconds * 0.075).r;
  return Flame(
      weight,
      clamp(1.62 * smoke * noise.r * inner + 0.85 * weight * coverage * outside, 0.0, 1.0),
      mask);
}
vec3 tintFlame(Flame f, vec3 ramp, vec3 burn, vec3 rim) {
  vec3 art = texture(_MainTex, uv).rgb;
  vec3 base =
      mix(art, clamp(mix(art, 1.0 - (1.0 - art) * burn, 0.18), 0.0, 1.0), absorption(f.mask));
  return mix(base, ramp, f.weight) + rim * f.mask * background(f.mask) * 1.15;
}
float spark(vec2 p, vec2 scale, float speed) {
  return texture(_FierySparks, polar(p) * scale + vec2(seconds * speed, 0)).r;
}
`;
