export default `uniform sampler2D _Shared_Smoke;
uniform float _TinyBlurFeather, _TinyEchoScale;
float edge(vec2 p) {
  vec2 distance = min(p, 1.0 - p);
  return smoothstep(0.0, max(0.001, _TinyBlurFeather), min(distance.x, distance.y));
}
vec3 effect() {
  vec2 p = (uv - 0.5) * vec2(0.5, 1);
  vec2 echo = p * _TinyEchoScale + 0.5;
  float fade = edge(echo);
  float mask = fade * fade * texture(_EnchantmentMask, echo).g;
  float pulse = max(0.0, 2.0 * sin(mask * 20.0 - seconds * 1.5) - 1.0);
  p += 0.5;
  float cover = edge(p);
  float cutout =
      1.0 -
      smoothstep(0.0, 0.02, _ItemCutoutBias + 0.01 - cover * texture(_EnchantmentMask, p).g);
  vec2 ray = uv * 2.0 - 1.0;
  vec2 smokeUV = vec2(length(ray), atan(ray.y, ray.x) / 3.14159265) + seconds * vec2(0.5, 0.1);
  float intensity =
      (1.0 - cutout * cover) * (1.0 - mask) * pow(pulse * clamp(mask * 3.0, 0.0, 1.0), 2.0);
  return texture(_MainTex, p).rgb +
         intensity * vec3(0.459119409, 0.878340483, 1) * texture(_Shared_Smoke, smokeUV).rgb;
}`;
