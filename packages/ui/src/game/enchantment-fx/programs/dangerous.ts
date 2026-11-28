export default `uniform sampler2D _Shared_Smoke;
uniform sampler2D _AngularAura;
vec3 effect() {
  vec2 p = (uv - 0.5) * _EnchantmentFlareScale + center();
  float drift = 11.5 + 2.0 * sin(seconds);
  vec2 first = polar(rotateUV(p, seconds * 0.06)) * vec2(0.6, 1.0);
  vec2 second = polar(rotateUV(p, -seconds * 0.03)) * vec2(1.0, 0.8);
  vec3 aura = 0.78 * (texture(_AngularAura, first - vec2(drift * 0.025, 0)).rgb +
                      texture(_AngularAura, second - vec2(drift * 0.05, 0)).rgb);
  vec3 ramp = clamp((aura.r - vec3(0.56, 0.74, 0.89)) *
                        vec3(5.6, 7.6, 15.0),
                    0.0, 1.0);
  vec3 red = vec3(0.48, 0.015, 0.005) + ramp.x * vec3(0.24, 0.025, 0.0);
  red = mix(mix(red, vec3(0.84, 0.02, 0.015), ramp.y), vec3(1.0, 0.20, 0.04), ramp.z);
  float mask = texture(_EnchantmentMask, uv).g;
  float back = background(mask);
  float rim = absorption(mask) + glow(mask);
  vec3 smoke = texture(_Shared_Smoke, uv * 2.0 - seconds * 0.16).rgb *
               texture(_Shared_Smoke, uv - seconds * 0.14).rgb;
  vec3 art = texture(_MainTex, uv).rgb;
  vec3 base = mix(art, vec3(0.72, 0.02, 0.01), clamp(2.1 * smoke * rim, 0.0, 0.80));
  return mix(base, red * (1.2 + aura), clamp(back * aura * 1.28, 0.0, 0.92));
}`;
