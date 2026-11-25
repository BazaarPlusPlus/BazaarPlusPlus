export default `uniform sampler2D _Shared_Smoke;
uniform sampler2D _AngularAura;
vec3 effect() {
  vec2 p = (uv - 0.5) * _EnchantmentFlareScale + center();
  float drift = 11.5 + 2.0 * sin(seconds);
  vec2 first = polar(rotateUV(p, seconds * 0.05)) * vec2(0.6, 1.0);
  vec2 second = polar(rotateUV(p, -seconds * 0.025)) * vec2(1.0, 0.8);
  vec3 aura = 0.6 * (texture(_AngularAura, first - vec2(drift * 0.025, 0)).rgb +
                     texture(_AngularAura, second - vec2(drift * 0.05, 0)).rgb);
  vec3 ramp = clamp((aura.r - vec3(0.7332418, 0.8646067, 0.9584497)) *
                        vec3(7.611805, 10.654956, 24.061438),
                    0.0, 1.0);
  vec3 red = vec3(0.3962264, 0.0121916, 0) + ramp.x * vec3(0.1603774, 0.0217477, 0);
  red = mix(mix(red, vec3(0.6415094, 0, 0.0125787), ramp.y), vec3(1, 0.0861411, 0), ramp.z);
  float mask = texture(_EnchantmentMask, uv).g;
  vec3 smoke = texture(_Shared_Smoke, uv * 2.0 - seconds * 0.15).rgb *
               texture(_Shared_Smoke, uv - seconds * 0.15).rgb;
  vec3 base = mix(texture(_MainTex, uv).rgb, vec3(0.6603774, 0, 0),
                  2.0 * smoke * (absorption(mask) + glow(mask)));
  return mix(base, red * (1.0 + aura), background(mask) * aura);
}`;
