import { flow } from './flow';

export default flow +
  `vec3 effect() {
  Flame f = flame();
  vec4 levels = clamp((f.heat - vec4(0.2610208, 0.6225376, 0.8425727, 0.9421988)) *
                          vec4(2.766047, 4.544523, 10.03652, 17.29769),
                      0.0, 1.0);
  vec3 ramp = vec3(0.5188679, 0.0835954, 0) + levels.x * vec3(0.3690553, 0.0693308, 0.0129830);
  ramp = mix(ramp, vec3(0.9357997, 0.5293208, 0.0406439), levels.y);
  ramp = mix(ramp, vec3(1, 0.6812068, 0), levels.z);
  ramp = mix(ramp, vec3(1, 0.7883974, 0.4402515), levels.w);
  vec3 base = tintFlame(f, ramp, vec3(1, 4.252336, 100000), vec3(1, 0.7950196, 0.1462264));
  vec2 pos = center() * vec2(0.5, 1);
  float sparks = spark(uv * vec2(1, 2) + pos + vec2(-0.75, 2), vec2(0.3, 12), -0.1333334) +
                 spark(uv * vec2(4, 8) + pos + vec2(-3.75, 3), vec2(0.1, 12), -0.2666666);
  vec3 sparkColor = vec3(0.7075472, 0.1142229, 0) +
                    clamp(sparks * 4.58458, 0.0, 1.0) * vec3(0.180376, 0.0387033, 0.0129830);
  vec3 steps = clamp((sparks - vec3(0.2181125, 0.5989318, 0.8926681)) *
                         vec3(2.625848, 3.404298, 9.316030),
                     0.0, 1.0);
  sparkColor = mix(sparkColor, vec3(0.9357997, 0.5293208, 0.0406439), steps.x);
  sparkColor = mix(sparkColor, vec3(0.9475806, 0.5847079, 0.0513628), steps.y);
  sparkColor = mix(sparkColor, vec3(1, 0.7879015, 0), steps.z);
  return mix(base, sparkColor, sparks * smoothstep(_ShinyGlintHeight, 1.0, uv.y));
}`;
