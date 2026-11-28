import zoomStars from './zoom-stars';

export default `${zoomStars}
uniform sampler2D _CardHighlight;
uniform vec4 _MainTex_ST;
uniform float _ShinyGlintHeight;
vec3 effect() {
  float mask = texture(_EnchantmentMask, uv).g;
  float back = background(mask);
  vec3 art = texture(_MainTex, uv).rgb;
  // Keep foreground clean; concentrate weightiness in the background silhouette and falling sparks.
  art = mix(art, vec3(0.40, 0.145, 0.04), glow(mask) * 0.85);
  float fade = 1.0 - smoothstep(_ShinyGlintHeight * 0.70, 1.0, uv.y);
  float bottomWeight = pow(1.0 - uv.y, 2.4);
  art = mix(art, art * vec3(0.95, 0.78, 0.62) + vec3(0.18, 0.06, 0.015), back * bottomWeight * 0.32);
  art += back * mask * fade * fade * vec3(0.95, 0.38, 0.09) * 1.35;
  vec2 flow = texture(_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw).rg;
  vec2 highlightUV = polar(flarePosition() + 0.5 * flow * vec2(aspect, 1));
  highlightUV.x = 1.2 * highlightUV.x + seconds * 0.15;
  art += (1.0 - back) * texture(_CardHighlight, highlightUV).rgb * vec3(1.10, 0.82, 0.52) * 1.25;
  return art + back * zoomStars(-0.22) * vec3(1.0, 0.50, 0.08) * 1.75;
}`;
