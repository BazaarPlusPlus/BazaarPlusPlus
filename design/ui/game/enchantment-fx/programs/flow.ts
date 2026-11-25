// Fiery and Toxic advect the red mask channel with the same two-phase flow map.
export const flow = `uniform sampler2D _ChaoticFlowmap, _Shared_Smoke, _FierySparks;
uniform vec4 _ChaoticFlowmap_ST;
uniform float _FieryDistortion, _FireDistortionScaling, _FireFlowDirectionRotation;
uniform float _FireFlowSpeed, _FireFlowStrenght, _ShinyGlintHeight, _Size;

struct Flame {
  float weight;
  float heat;
  float mask;
};
Flame flame() {
  vec2 mapUV = rotateUV((uv - 0.5) * vec2(aspect, 1) * _FireDistortionScaling + center(),
                        _FireFlowDirectionRotation) +
               0.5;
  vec2 flowUV =
      texture(_ChaoticFlowmap, mapUV).gr + rotateUV(uv - 0.5, _FireFlowDirectionRotation) + 0.5;
  vec2 velocity = flowUV * _FireFlowStrenght * (1.0 - 2.0 * flowUV);
  vec2 noiseUV =
      textureLod(_ChaoticFlowmap, uv * _ChaoticFlowmap_ST.xy + _ChaoticFlowmap_ST.zw, 0.0).rg;
  vec2 noise = texture(_Shared_Smoke,
                       noiseUV * vec2(0.5, 1) + vec2(0.25, 0) + seconds * vec2(-0.15, 0.15))
                   .rg;
  float inner = texture(_EnchantmentMask, uv * 0.85).g;
  vec2 distorted = (uv + noise * _FieryDistortion * inner) / _Size;
  float phase = fract(seconds * _FireFlowSpeed);
  float first = texture(_EnchantmentMask, distorted + velocity * phase).r;
  float second = texture(_EnchantmentMask, distorted + velocity * fract(phase + 0.5)).r;
  float mask = texture(_EnchantmentMask, uv).g, outside = background(mask);
  float weight = mix(first, second, abs(2.0 * phase - 1.0)) * outside;
  vec2 sampleUV = uv + center();
  float coverage = 0.3333 * (texture(_EnchantmentMask, sampleUV).r +
                             texture(_EnchantmentMask, 0.8 * sampleUV).r +
                             texture(_EnchantmentMask, 0.9 * sampleUV).r);
  float smoke =
      texture(_Shared_Smoke, uv * vec2(0.75, 1.5) + vec2(0.25, 0) - seconds * 0.075).r;
  return Flame(
      weight,
      clamp(1.5 * smoke * noise.r * inner + 0.75 * weight * coverage * outside, 0.0, 1.0),
      mask);
}
vec3 tintFlame(Flame f, vec3 ramp, vec3 burn, vec3 rim) {
  vec3 art = texture(_MainTex, uv).rgb;
  vec3 base =
      mix(art, clamp(mix(art, 1.0 - (1.0 - art) * burn, 0.2), 0.0, 1.0), absorption(f.mask));
  return mix(base, ramp, f.weight) + rim * f.mask * background(f.mask);
}
float spark(vec2 p, vec2 scale, float speed) {
  return texture(_FierySparks, polar(p) * scale + vec2(seconds * speed, 0)).r;
}`;
