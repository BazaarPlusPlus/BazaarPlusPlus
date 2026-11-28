// Medium (2->1 slot) and Large (3->2 slots) Tiny enchantment shader:
// preserves the card's original art aspect inside the narrower frame via _TinyScalar
// and _TinyOffsetter, frames out-of-bounds margins with a radial blur, pulses dual
// holographic shrink echoes, and animates floating 3D isometric _TinyBlocks cubes.
export default `uniform sampler2D _Shared_Smoke, _TinyBlocks;
uniform float _TinyScalar, _TinyOffsetterX, _TinyOffsetterY;
uniform float _TinyBlurFeather, _TinyBlurStrength, _TinyTransitionWidth;
uniform float _TinyEchoScale, _TinyEchoScaleOverlay;
uniform float _TinyBlocksOpacity, _TinyBlocksEdge, _TinyBlocksFace;
uniform float _TinyBlocksSpeed, _TinyBlocksDensity, _TinyBlocksShell;
uniform float _TinyBlocksGlow, _TinyBlocksSizeVariation, _TinyBlocksFaceVariation;
uniform float _TinyBlocksDrift, _CrystalUOffset, _CrystalVOffset;
uniform vec4 _TinyBlocksSeed;

float tinyEdge(vec2 p, float feather) {
  vec2 d = min(p, 1.0 - p);
  return smoothstep(0.0, max(0.001, feather), min(d.x, d.y));
}

float tinyHash(float n) {
  return fract(sin(n) * 43758.5453123);
}

vec3 blockEdgeColor(int palette) {
  if (palette == 0) return vec3(0.32, 0.86, 1.0);
  if (palette == 1) return vec3(1.0, 0.50, 0.86);
  return vec3(1.0, 0.80, 0.32);
}

vec3 blockFaceColor(int palette, int face) {
  if (palette == 0) {
    if (face == 0) return vec3(0.22, 0.56, 0.88);
    if (face == 1) return vec3(0.16, 0.44, 0.72);
    return vec3(0.11, 0.32, 0.58);
  }
  if (palette == 1) {
    if (face == 0) return vec3(0.84, 0.28, 0.62);
    if (face == 1) return vec3(0.68, 0.18, 0.50);
    return vec3(0.52, 0.12, 0.38);
  }
  if (face == 0) return vec3(0.88, 0.58, 0.18);
  if (face == 1) return vec3(0.72, 0.44, 0.12);
  return vec3(0.56, 0.32, 0.08);
}

vec3 sampleBlurred(vec2 p, float amount) {
  vec2 clamped = clamp(p, vec2(0.005), vec2(0.995));
  if (amount <= 0.001) return texture(_MainTex, clamped).rgb;
  vec2 dir = clamped - 0.5;
  float len = max(length(dir), 0.0001);
  vec2 stepVec = (dir / len) * min(len * 0.85, amount);
  vec3 acc = vec3(0.0);
  for (int i = 0; i < 8; i++) {
    float t = (float(i) + 0.5) / 8.0;
    acc += texture(_MainTex, clamp(clamped - stepVec * t, vec2(0.005), vec2(0.995))).rgb;
  }
  return acc * 0.125;
}

vec3 compositeBlocks(vec3 color, float tinyAspect) {
  float opacity = clamp(_TinyBlocksOpacity, 0.0, 1.0) * 0.88;
  if (opacity <= 0.001) return color;
  float seedBase = _TinyBlocksSeed.x * 0.017 + _CrystalUOffset * 37.0 + _CrystalVOffset * 73.0;
  float speed = max(_TinyBlocksSpeed, 0.25);
  float edgeStrength = clamp(_TinyBlocksEdge * 1.0, 0.0, 1.0);
  float faceStrength = clamp(_TinyBlocksFace * 1.55, 0.0, 1.0);
  float glowStrength = max(_TinyBlocksGlow, 0.75) * 0.90;
  float driftMax = max(_TinyBlocksDrift, 0.04) * 1.35;
  float sizeVar = clamp(_TinyBlocksSizeVariation, 0.0, 0.6);
  int count = int(clamp(round(11.0 * max(_TinyBlocksDensity, 0.72)), 8.0, 15.0));
  vec2 screenUV = vec2(uv.x, 1.0 - uv.y);

  for (int i = 0; i < 15; i++) {
    if (i >= count) break;
    float fi = float(i);
    float s0 = seedBase + fi * 19.37;
    float cycle = 3.4 * (1.15 + 0.45 * tinyHash(s0 + 1.3));
    float lifetime = cycle * (0.76 + 0.20 * tinyHash(s0 + 2.7));
    float tRaw = seconds * speed + cycle * tinyHash(s0 + 4.1);
    float gen = floor(tRaw / cycle);
    float age = mod(tRaw, cycle) / lifetime;
    if (age < 0.0 || age >= 1.0) continue;

    float fade = smoothstep(0.0, 0.15, age) * (1.0 - smoothstep(0.56, 1.0, age));
    if (fade <= 0.002) continue;

    float gSeed = s0 + gen * 53.19;
    int band = int(mod(fi + gen, 4.0));
    float rx = tinyHash(gSeed + 6.1);
    float ry = tinyHash(gSeed + 8.3);
    vec2 origin;
    if (band == 0) origin = vec2(0.08 + rx * 0.22, 0.10 + ry * 0.80);
    else if (band == 1) origin = vec2(0.70 + rx * 0.22, 0.10 + ry * 0.80);
    else if (band == 2) origin = vec2(0.14 + rx * 0.72, 0.08 + ry * 0.16);
    else origin = vec2(0.14 + rx * 0.72, 0.76 + ry * 0.16);

    vec2 outward = origin - 0.5;
    float outLen = max(length(outward), 0.01);
    vec2 dir = (outward / outLen) * vec2(1.0 / tinyAspect, 1.0);
    vec2 pos = origin + dir * (smoothstep(0.0, 1.0, age) * driftMax);

    float scale = (0.12 + 0.048 * tinyHash(gSeed + 10.7)) * (1.0 + (tinyHash(gSeed + 12.9) - 0.5) * sizeVar);
    vec2 halfSize = vec2(scale / tinyAspect, scale) * 0.5;
    vec2 local = (screenUV - pos) / (halfSize * 2.0) + 0.5;
    if (local.x <= 0.0 || local.x >= 1.0 || local.y <= 0.0 || local.y >= 1.0) continue;

    int sprite = int(floor(tinyHash(gSeed + 14.3) * 11.0));
    int palette = int(mod(fi + floor(tinyHash(gSeed + 16.7) * 3.0), 3.0));
    float col = float(sprite & 3);
    float row = float(sprite >> 2);
    vec2 atlasUV = vec2((col + local.x) * 0.125, 1.0 - (row + local.y) * 0.25);

    vec4 baseTex = texture(_TinyBlocks, atlasUV);
    vec4 glowTex = texture(_TinyBlocks, atlasUV + vec2(0.5, 0.0));

    vec3 edgeCol = blockEdgeColor(palette);
    vec3 f0 = blockFaceColor(palette, 0);
    vec3 f1 = blockFaceColor(palette, 1);
    vec3 f2 = blockFaceColor(palette, 2);
    float shade = mix(0.84, 1.12, glowTex.a * (0.5 + 0.5 * _TinyBlocksFaceVariation));
    vec3 faceRgb = (baseTex.r * f0 + baseTex.g * f1 + baseTex.b * f2) * shade;
    float faceAlpha = clamp((baseTex.r + baseTex.g + baseTex.b) * faceStrength, 0.0, 1.0);
    float wireAlpha = clamp(baseTex.a * edgeStrength, 0.0, 1.0);

    vec3 cubeRgb = mix(faceRgb, edgeCol, wireAlpha);
    float cubeAlpha = clamp(max(faceAlpha, wireAlpha) * fade * opacity, 0.0, 0.90);
    color = mix(color, cubeRgb, cubeAlpha);

    float halo = dot(glowTex.rg, vec2(0.92, 1.12)) + glowTex.b * 0.85;
    float shellPulse = 1.0 + 0.35 * _TinyBlocksShell * sin(age * 3.14159265);
    color += edgeCol * halo * fade * opacity * glowStrength * shellPulse * 0.38;
  }
  return color;
}

vec3 effect() {
  // Medium (aspect 1.0 -> 0.5) scales Y by 2.0; Large (aspect 1.5 -> 1.0) scales Y by 1.5.
  float tinyAspect = max(0.5, aspect - 0.5);
  float yScale = aspect / tinyAspect;
  float yShift = 0.5 - 0.5 * yScale;
  float scalar = _TinyScalar > 0.01 ? _TinyScalar : 0.68;
  vec2 p = uv * vec2(scalar, scalar * yScale) + vec2(_TinyOffsetterX, _TinyOffsetterY + yShift);

  float trans = max(_TinyTransitionWidth, 0.04);
  float cover = tinyEdge(p, _TinyBlurFeather);
  float marginFade = tinyEdge(p, trans * 1.15);
  float rawMask = texture(_EnchantmentMask, clamp(p, 0.0, 1.0)).g;
  float itemCutout = 1.0 - smoothstep(0.0, 0.025, _ItemCutoutBias + 0.01 - cover * rawMask);
  float blurWeight = (1.0 - marginFade) * (1.0 - 0.85 * itemCutout);

  vec3 sharp = texture(_MainTex, clamp(p, 0.0, 1.0)).rgb;
  vec3 blurred = sampleBlurred(p, max(_TinyBlurStrength, 0.14) * blurWeight);
  vec3 base = mix(sharp, blurred, blurWeight);
  vec3 tinyPink = vec3(0.94, 0.62, 1.0);
  vec3 tinyCyan = vec3(0.46, 0.88, 1.0);
  base = mix(base, base * vec3(0.88, 0.88, 0.96) + tinyPink * 0.05, blurWeight * 0.20);

  // Dual holographic shrink echoes radiating around the shrunken silhouette.
  float echoScaleA = _TinyEchoScale > 0.05 ? _TinyEchoScale : 0.78;
  float echoScaleB = _TinyEchoScaleOverlay > 0.05 ? _TinyEchoScaleOverlay : 0.86;
  vec2 echoA = (p - 0.5) * echoScaleA + 0.5;
  vec2 echoB = (p - 0.5) * echoScaleB + 0.5;
  float maskA = pow(tinyEdge(echoA, _TinyBlurFeather), 2.0) * texture(_EnchantmentMask, clamp(echoA, 0.0, 1.0)).g;
  float maskB = pow(tinyEdge(echoB, _TinyBlurFeather), 2.0) * texture(_EnchantmentMask, clamp(echoB, 0.0, 1.0)).g;
  float waveA = 0.25 + 0.75 * pow(0.5 + 0.5 * sin(maskA * 18.0 - seconds * 2.0), 2.0);
  float waveB = 0.25 + 0.75 * pow(0.5 + 0.5 * sin(maskB * 22.0 - seconds * 2.8), 2.0);

  vec2 ray = (uv - 0.5) * vec2(tinyAspect, 1.0) * 2.0;
  vec2 smokeUV = vec2(length(ray), atan(ray.y, ray.x) / 3.14159265) + seconds * vec2(0.45, 0.12);
  vec3 smoke = 0.42 + 0.58 * texture(_Shared_Smoke, smokeUV).rgb;

  float outerRing = (1.0 - itemCutout * cover) * clamp(maskA * 2.6, 0.0, 1.0) * (1.0 - maskA * 0.68);
  float innerRing = (1.0 - itemCutout * cover) * clamp(maskB * 2.4, 0.0, 1.0) * (1.0 - maskB * 0.68);
  base += outerRing * waveA * tinyCyan * smoke * 0.72;
  base += innerRing * waveB * tinyPink * smoke * 0.48;

  return compositeBlocks(base, tinyAspect);
}`;
