export default `uniform float _PrismaticUVSpeed;
uniform sampler2D _PrismaticNoise;
uniform sampler2D _PrismaticRainbow;
uniform sampler2D _Prismatic_Flares;
vec3 effect() {
  vec4 flares = texture(_Prismatic_Flares, cardUV(uv));
  vec2 p = (uv * vec2(aspect, 0.25) - vec2(0, 0.3)) * 2.0 - 1.0;
  vec2 noiseUV = polar(p.yx) * vec2(1, 2) + seconds * _PrismaticUVSpeed * 1.15;
  vec4 noise = texture(_PrismaticNoise, (noiseUV + 1.0) * vec2(1, 2));
  vec3 hue = shiftHue(noise.rgb, seconds * 0.42, 0.08);
  float pulse = 0.35 + 0.65 * (0.5 + 0.5 * sin(seconds * 1.25));
  float flarePulse = 0.30 + 0.70 * (0.5 + 0.5 * sin(seconds * 2.0));
  vec4 rainbow = texture(_PrismaticRainbow, rotateUV(flarePosition(), seconds * 0.16) + 0.5);
  vec3 base = texture(_MainTex, uv).rgb;
  float mask = texture(_EnchantmentMask, uv).g;
  float back = background(mask);
  float rim = absorption(mask) + glow(mask);
  vec3 tinted = mix(base, base * rainbow.rgb * 1.15, pulse * rainbow.a * back);
  vec3 flareColor = shiftHue(flares.rgb, seconds * 0.42, 0.85 + pulse * 0.50);
  base = mix(tinted + 0.72 * pulse * rainbow.a * rainbow.rgb * back, flareColor * (tinted + 0.15),
             flares.a * flarePulse * 0.65 * (0.25 + 0.75 * back));
  float weight = clamp(noise.a * rim * 1.25, 0.0, 1.0);
  base += 0.24 * weight * hue;
  return mix(base, clamp(2.8 * base * hue + base * base * (1.0 - 2.6 * hue), 0.0, 1.0), weight * 0.75);
}`;
