import { cellular } from './cellular';

export default `${cellular}
uniform sampler2D _TemperedCrystal, _OffsetMask, _TriangleTex;
uniform vec4 _OffsetMask_ST;
uniform float _OutlineTempered;
vec3 effect() {
  float mask = texture(_EnchantmentMask,uv).g;
  float back = background(mask);
  vec3 art = texture(_MainTex,uv).rgb;
  // The reference fragment binds a clamped sampler to the scrolling crystal lookup.
  vec2 texel = 0.5/vec2(textureSize(_TemperedCrystal,0));
  vec2 crystalUV = uv*vec2(1.35,2.7)+vec2(0.25,0.025*seconds);
  float crystal = texture(_TemperedCrystal,clamp(crystalUV,texel,1.0-texel)).r;
  float pulse = max(0.0,sin((0.5*uv.x+crystal)*3.0+1.4*seconds));
  vec3 shine = pulse*vec3(3.101734,2.258628,0.829080);
  vec3 overlay = art*shine + art*art*(1.0-shine);
  art = mix(art,clamp(mix(art,overlay,1.550867*pulse),0.0,1.0),1.0-back);
  vec3 gold = vec3(1,0.734360,0.138365);
  art += gold*(glow(mask)*_OutlineTempered*cellularLight() + 0.4*absorption(mask));
  vec3 offset = texture(_OffsetMask,uv*_OffsetMask_ST.xy+_OffsetMask_ST.zw).rgb;
  vec2 p = rotateUV(12.5*(uv-offset.rg),3.0*(offset.b-0.5)*seconds);
  float width = 10.0*sin(dot(offset,vec3(10))+0.55*seconds)-9.0;
  vec3 triangle = textureLod(_TriangleTex,vec2(p.x/width,p.y)+0.5,0.0).rgb;
  width = max(width,0.0);
  vec3 edge = triangle.g*vec3(1.05,0.731152,0.500262)*width*width;
  edge += (triangle.r*width + triangle.b*width*width)*vec3(0.501961,0.345873,0.160784);
  return art + back*edge;
}`;
