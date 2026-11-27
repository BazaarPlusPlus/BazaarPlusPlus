// The lattice hash amplifies sine by 43758. Range reduction keeps its cells stable
// on WebGL implementations whose native sine loses precision at large angles.
export const cellular = `
float latticeSin(float angle) {
  float quadrant = roundEven(angle * 0.6366197723675814);
  float x = ((angle - quadrant * 1.5703125) - quadrant * 0.0004837512969970703)
            - quadrant * 7.549789415861596e-8;
  float u = x*x;
  float sine = x + x*u*(-0.166666667 + u*(0.008333333 + u*(-0.000198413 + u*0.000002756)));
  float cosine = 1.0 + u*(-0.5 + u*(0.041666667 + u*(-0.001388889 + u*0.000024802)));
  int sector = int(quadrant) & 3;
  return sector == 0 ? sine : sector == 1 ? cosine : sector == 2 ? -sine : -cosine;
}
float cellularLight() {
  vec2 p = uv * vec2(10.0, 0.5) - vec2(seconds * 0.25, 0);
  vec2 nearest = vec2(8);
  for (int y = -1; y <= 1; y++) {
    for (int x = -1; x <= 1; x++) {
      vec2 offset = vec2(x,y);
      vec2 cell = floor(p) + offset;
      vec2 seed = fract(vec2(latticeSin(dot(cell, vec2(127.1,311.7))),
                             latticeSin(dot(cell, vec2(269.5,183.3)))) * 43758.5469);
      vec2 delta = fract(p) - offset - (0.5 + 0.5*sin(seconds + 6.2831*seed));
      float distanceSquared = 0.5*dot(delta,delta);
      nearest = vec2(min(nearest.x,distanceSquared),
                     min(nearest.y,max(nearest.x,distanceSquared)));
    }
  }
  return smoothstep(0.15,0.90,0.5*(nearest.x+nearest.y));
}
`;

export const runes = `
uniform sampler2D _MysticalRunes;
uniform vec4 _MysticalRunes_ST;
vec3 runeChannels(float back) {
  vec2 p = polar((uv - 0.5) * vec2(aspect,1));
  float time = seconds + 91.0*_AbsorptionMaskMax;
  vec2 runeUV = p * vec2(0.135,3.0) + vec2(-0.05,0.015)*time + vec2(0,1.24);
  float coverage = texture(_MysticalRunes,uv*_MysticalRunes_ST.xy+_MysticalRunes_ST.zw).g;
  return clamp(back*coverage*texture(_MysticalRunes,runeUV).rgb,0.0,1.0);
}
`;
