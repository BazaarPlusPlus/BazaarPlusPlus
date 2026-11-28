import { flow } from './flow';

export default flow +
  `vec3 effect() {
  Flame f = flame();
  vec4 levels = clamp((f.heat - vec4(0.22, 0.56, 0.79, 0.91)) *
                          vec4(2.766047, 4.544523, 10.03652, 17.29769),
                      0.0, 1.0);
  vec3 ramp = vec3(0.58, 0.10, 0.0) + levels.x * vec3(0.38, 0.09, 0.015);
  ramp = mix(ramp, vec3(0.96, 0.55, 0.04), levels.y);
  ramp = mix(ramp, vec3(1.0, 0.72, 0.02), levels.z);
  ramp = mix(ramp, vec3(1.0, 0.86, 0.52), levels.w);
  vec3 base = tintFlame(f, ramp, vec3(1.0, 4.252336, 100000.0), vec3(1.0, 0.78, 0.16));
  vec2 pos = center() * vec2(aspect, 1.0);
  float sparks = spark(uv * vec2(1, 2) + pos + vec2(-0.75, 2), vec2(0.3, 12), -0.14) +
                 spark(uv * vec2(4, 8) + pos + vec2(-3.75, 3), vec2(0.1, 12), -0.28);
  vec3 sparkColor = vec3(0.80, 0.16, 0.0) +
                    clamp(sparks * 4.58458, 0.0, 1.0) * vec3(0.18, 0.05, 0.012);
  vec3 steps = clamp((sparks - vec3(0.20, 0.56, 0.86)) *
                         vec3(2.625848, 3.404298, 9.316030),
                     0.0, 1.0);
  sparkColor = mix(sparkColor, vec3(0.98, 0.56, 0.04), steps.x);
  sparkColor = mix(sparkColor, vec3(1.0, 0.66, 0.06), steps.y);
  sparkColor = mix(sparkColor, vec3(1.0, 0.86, 0.12), steps.z);
  float sparkFade = mix(0.30, 1.0, smoothstep(_ShinyGlintHeight * 0.70, 1.0, uv.y));
  return mix(base, sparkColor * 1.1, clamp(sparks * 1.35 * sparkFade, 0.0, 1.0));
}`;
