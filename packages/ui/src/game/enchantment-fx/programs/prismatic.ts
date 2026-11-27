export default `uniform vec2 _PrismaticUVSpeed;
uniform sampler2D _PrismaticNoise;
uniform sampler2D _PrismaticRainbow;
uniform sampler2D _Prismatic_Flares;
vec3 effect() {
  vec4 flares = texture(_Prismatic_Flares, uv * vec2(0.5, 1) + vec2(0.25, 0));
  vec2 p = (uv * vec2(0.5, 0.25) - vec2(0, 0.3)) * 2.0 - 1.0;
  vec2 noiseUV = polar(p.yx) * vec2(1, 2) + seconds * _PrismaticUVSpeed;
  vec4 noise = texture(_PrismaticNoise, (noiseUV + 1.0) * vec2(1, 2));
  vec3 hue = shiftHue(noise.rgb, seconds * 0.4, 0.0);
  float pulse = max(0.0, sin(seconds));
  vec4 rainbow = texture(_PrismaticRainbow, rotateUV(flarePosition(), seconds * 0.15) + 0.5);
  vec3 base = texture(_MainTex, uv).rgb;
  float mask = texture(_EnchantmentMask, uv).g;
  vec3 tinted = mix(base, base * rainbow.rgb, pulse * rainbow.a * background(mask));
  vec3 flareColor = shiftHue(flares.rgb, seconds * 0.4, 1.0 + pulse);
  base = mix(tinted + 0.8 * pulse * rainbow.a * rainbow.rgb, flareColor * tinted,
             flares.a * max(0.0, sin(seconds * 2.0)));
  float weight = noise.a * (absorption(mask) + glow(mask));
  base += 0.15 * weight * noise.rgb;
  return mix(base, 3.0 * base * hue + base * base * (1.0 - 3.0 * hue), weight);
}`;
