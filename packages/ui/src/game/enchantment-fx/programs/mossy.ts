import { cellular, runes } from './cellular';

export default `${cellular}
${runes}
uniform sampler2D _Shared_Smoke, _Mossy_Noise, _DazzlingGlintMap, _HeartTexture;
uniform float _RandomnessWiggle;
float sparks(vec2 p, float core, bool rays) {
  float sum = 0.0;
  for (int y = -1; y <= 1; y++) {
    for (int x = -1; x <= 1; x++) {
      vec2 neighbor = vec2(x,y);
      float seed = fract(latticeSin(dot(floor(p)+neighbor,vec2(12.9898005,78.2330017))) * 43758.5508);
      vec2 delta = fract(p)-neighbor-vec2(seed, fract(seed*34.5));
      float radius = length(delta);
      float intensity = core/max(radius,1e-8);
      if (rays) {
        vec2 ray = delta * 0.30;
        vec2 crossed = rotateUV(delta,0.8)*1.5;
        intensity += 0.1*(max(0.0,1.0-abs(ray.x*ray.y)*1000.0)
                         +max(0.0,1.0-abs(crossed.x*crossed.y)*1000.0));
      }
      sum += fract(seed*345.6)*min(1.0,intensity*smoothstep(0.0,1.0,max(0.0,1.0-2.0*radius)));
    }
  }
  return sum;
}
vec3 effect() {
  float mask = texture(_EnchantmentMask,uv).g;
  float back = background(mask);
  vec3 art = texture(_MainTex,uv).rgb;
  vec3 smoke = 2.0*texture(_Shared_Smoke,2.0*uv-0.15*seconds).rgb
                  *texture(_Shared_Smoke,uv-0.15*seconds).rgb;
  smoke *= absorption(mask) + glow(mask);
  vec3 noise = texture(_Mossy_Noise,uv*vec2(1,2)+vec2(0.25,0)).rgb * 0.83018869;
  vec3 overlay = mix(2.0*art*noise,1.0-2.0*(1.0-art)*(1.0-noise),greaterThan(noise,vec3(0.5)));
  vec2 smokeUV = (uv + _AbsorptionMaskMax*vec2(169,91))*vec2(aspect,1)+vec2(0.25,0);
  art = clamp(mix(art,overlay,smoothstep(0.35,0.90,(1.0-back)*texture(_Shared_Smoke,smokeUV).r)),0.0,1.0);
  vec2 p = (uv-0.5)*vec2(aspect,1);
  float light = cellularLight();
  float stars = sparks(rotateUV(p,seconds*0.05)*5.0,0.03,true)
              + sparks(rotateUV(p,1.0-seconds*0.05)*4.0,0.01,false);
  art += light*stars*vec3(1.352941,2.329412,0.164706);
  vec3 green = vec3(0.472706,0.917647,0.086275);
  art = mix(art,green,smoke) + mask*light*vec3(0.45098,0.77647,0.054902);
  float rune = runeChannels(back).r;
  vec3 color = mix(vec3(0.152941,0.135459,0.019608),vec3(0.179641,0.247059,0),
                   clamp((rune-0.026047)*5.890439,0.0,1.0));
  color = mix(color,vec3(0.572969,0.881401,0.112848),clamp((rune-0.195804)*1.704836,0.0,1.0));
  color = mix(color,vec3(0.618228,1,0),max(0.0,(rune-0.782361)*4.594545));
  art = mix(art,art*clamp(color,0.0,1.0),rune) + 1.5*rune*color;
  vec2 glintUV = (uv*0.9 + _AbsorptionMaskMax*vec2(39,91) + _RandomnessWiggle)*vec2(aspect,1)+vec2(0.25,0);
  vec3 glint = textureLod(_DazzlingGlintMap,glintUV,0.0).rgb;
  float pulse = max(0.0,(sin(10.0*glint.b + 0.8*seconds)*5.0-2.0)/3.0);
  return art + green*pulse*min(1.0,100.0*glint.b)*texture(_HeartTexture,glint.rg).g;
}`;
