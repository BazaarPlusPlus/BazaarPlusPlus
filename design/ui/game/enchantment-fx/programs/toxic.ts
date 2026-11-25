import { flow } from './flow';

export default flow +
  `vec3 poison(float low, float mid, float high, vec3 end) {
  vec3 ramp = vec3(0.0207882, 0.1137255, 0) + low * vec3(-0.0207882, -0.0005180, 0.0383164);
  return mix(mix(ramp, vec3(0.0112139, 0.2547170, 0.0749344), mid), end, high);
}
vec3 effect() {
  Flame f = flame();
  vec3 ramp =
      poison(min(1.0, f.heat * 3.232998), clamp((f.heat - 0.3093004) * 2.151252, 0.0, 1.0),
             max(0.0, (f.heat - 0.7741359) * 4.427245), vec3(0.0783196, 0.5660378, 0.1091878));
  vec3 base = tintFlame(f, ramp, vec3(3.011698, 1, 3.854545), vec3(0, 0.7075472, 0.1516172));
  float sparks = spark(uv * vec2(1.5, 2) + center() * vec2(0.5, 1) + vec2(-1.25, 2),
                       vec2(0.4, 7), -0.03333334);
  vec2 steps =
      clamp((sparks * 1.5 - vec2(0.7115587, 0.9332570)) * vec2(4.510431, 14.980597), 0.0, 1.0);
  vec3 sparkColor = poison(min(1.0, sparks * 2.108018), steps.x, steps.y,
                           vec3(0.0369941, 0.4056604, 0.0607790));
  return mix(base, sparkColor, sparks * 1.5 * smoothstep(_ShinyGlintHeight, 1.0, uv.y));
}`;
