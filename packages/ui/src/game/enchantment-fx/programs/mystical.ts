import { cellular, runes } from './cellular';

export default `${cellular}
${runes}
uniform sampler2D _MysticalBorealLines;
uniform vec4 _MysticalHue;
vec3 effect() {
  vec2 mask = texture(_EnchantmentMask,uv).gb;
  vec3 art = texture(_MainTex,uv).rgb;
  float back = background(mask.x);
  float outline = glow(mask.x);
  float light = dot(art,vec3(0.299,0.587,0.114)) + cellularLight();
  art = mix(art,light*_MysticalHue.rgb,0.5*mask.y);
  vec2 linesUV = polar(flarePosition()) * vec2(0.6,1);
  linesUV.x -= 0.1*(seconds + 221.0*_AbsorptionMaskMax);
  vec4 lines = texture(_MysticalBorealLines,linesUV);
  art = mix(art,lines.rgb,(back-outline)*lines.a);
  art = mix(art,art*_MysticalHue.rgb,outline);
  float rune = runeChannels(back).b;
  vec3 color = mix(vec3(0,0.01194786,0.1603774),vec3(0.04937188,0,0.24528299),
                   clamp(rune*8.474,0.0,1.0));
  color = mix(color,vec3(0.646634,0.119497,1),clamp((rune-0.117998)*1.297732,0.0,1.0));
  color = mix(color,vec3(0.965429,0.364780,1),max(0.0,(rune-0.888563)*8.972898));
  return mix(art,art*min(color,vec3(1)),rune) + color*(rune*0.85);
}`;
