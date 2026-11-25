import { cellular } from './cellular';
import zoomStars from './zoom-stars';

export default `${cellular}
${zoomStars}
uniform sampler2D _Golden_FXFlare;
uniform vec4 _Golden_FXFlare_ST;
uniform float _GoldenBrightness, _GoldenContrast;
vec3 effect() {
  vec2 mask = texture(_EnchantmentMask,uv).gb;
  vec3 art = texture(_MainTex,uv).rgb;
  float tone = (mask.y*dot(art,vec3(0.299,0.587,0.114))+_GoldenBrightness)*_GoldenContrast
             + 1.35*cellularLight();
  vec3 gold = vec3(0.452830,0.284082,0) + vec3(0.358935,0.104153,0)*clamp(tone*2.833253,0.0,1.0);
  gold = mix(gold,vec3(0.968628,0.835294,0),clamp((tone-0.352941)*4.856726,0.0,1.0));
  gold = mix(gold,vec3(0.988235,0.933333,0.192157),clamp((tone-0.558831)*4.146301,0.0,1.0));
  gold = mix(gold,vec3(0.996078,0.988235,0.623529),clamp((tone-0.8)*4.99975,0.0,1.0));
  gold = mix(art,gold,mask.y*0.756);
  vec2 p = uv*_Golden_FXFlare_ST.xy + _Golden_FXFlare_ST.zw - 0.5;
  vec4 flare = texture(_Golden_FXFlare,rotateUV((p*_EnchantmentFlareScale+center())*vec2(aspect,1),seconds*0.05)+0.5);
  float back = background(mask.x);
  vec3 shine = mix(art,flare.rgb*art,flare.a) + flare.a*flare.rgb*(sin(seconds)+1.0)*0.25;
  return mix(gold,shine,flare.a*back)
       + back*zoomStars(0.1)*vec3(1,0.810056,0.25)
       + back*mask.x*vec3(0.8,0.648045,0.2);
}`;
