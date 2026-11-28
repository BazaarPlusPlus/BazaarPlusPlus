import { flow } from './flow';

export default flow +
  `vec3 poison(float low, float mid, float high, vec3 end) {
  vec3 ramp = vec3(0.025, 0.145, 0.0) + low * vec3(0.0, 0.03, 0.035);
  return mix(mix(ramp, vec3(0.03, 0.36, 0.085), mid), end, high);
}
vec3 effect() {
  Flame f = flame();
  vec3 ramp =
      poison(min(1.0, f.heat * 3.232998), clamp((f.heat - 0.27) * 2.15, 0.0, 1.0),
             max(0.0, (f.heat - 0.72) * 4.4), vec3(0.15, 0.76, 0.14));
  vec3 base = tintFlame(f, ramp, vec3(3.011698, 1.0, 3.854545), vec3(0.08, 0.78, 0.15));
  float sparks = spark(uv * vec2(1.5, 2) + center() * vec2(aspect, 1.0) + vec2(-1.25, 2),
                       vec2(0.4, 7), -0.04);
  vec2 steps =
      clamp((sparks * 1.60 - vec2(0.64, 0.87)) * vec2(4.5, 13.5), 0.0, 1.0);
  vec3 sparkColor = poison(min(1.0, sparks * 2.25), steps.x, steps.y,
                           vec3(0.20, 0.78, 0.14));
  float sparkFade = mix(0.28, 1.0, smoothstep(_ShinyGlintHeight * 0.70, 1.0, uv.y));
  return mix(base, sparkColor * 1.08, clamp(sparks * 1.65 * sparkFade, 0.0, 1.0));
}`;
