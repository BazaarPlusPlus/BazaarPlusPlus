import zoomStars from './zoom-stars';

export default `${zoomStars}
uniform sampler2D _CardHighlight;
uniform vec4 _MainTex_ST;
uniform float _ShinyGlintHeight;
vec3 effect() {
  float mask = texture(_EnchantmentMask,uv).g;
  float back = background(mask);
  vec3 art = texture(_MainTex,uv).rgb;
  art = mix(art,vec3(0.399057,0.144644,0.040157),glow(mask)*0.9);
  float fade = 1.0-smoothstep(_ShinyGlintHeight,1.0,uv.y);
  art += back*mask*fade*fade*fade*vec3(0.886792,0.321432,0.089237);
  vec2 flow = texture(_MainTex,uv*_MainTex_ST.xy+_MainTex_ST.zw).rg;
  vec2 highlightUV = polar(flarePosition() + 0.5*flow*vec2(aspect,1));
  highlightUV.x = 1.2*highlightUV.x + seconds*0.136;
  art += (1.0-back)*texture(_CardHighlight,highlightUV).rgb;
  return art + back*zoomStars(-0.2)*vec3(1,0.454896,0);
}`;
