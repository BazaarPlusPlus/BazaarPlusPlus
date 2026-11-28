import { cellular, sparks } from './cellular';

export default `${cellular}
${sparks}
uniform sampler2D _IcyOverlayNoise, _IcyShards;
uniform float _IceCrystalsScale, _CrystalUOffset, _CrystalVOffset;
vec3 effect() {
  float mask = texture(_EnchantmentMask, uv).g;
  float back = background(mask);
  float rim = absorption(mask);
  float halo = glow(mask);
  float light = cellularLight();
  vec3 art = texture(_MainTex, uv).rgb;
  // Rotating frost texture clings only to the silhouette rim, keeping interior art crisp.
  vec2 noiseUV = rotateUV(cardUV(uv) - 0.5, seconds * 0.035) + 0.5;
  vec4 frost = texture(_IcyOverlayNoise, noiseUV);
  vec3 frostTint = frost.rgb * vec3(0.96, 1.38, 1.58);
  float frostMix = clamp(rim * 0.52 * (0.40 + 0.60 * frost.a), 0.0, 0.52);
  art = mix(art, mix(art, frostTint, 0.58), frostMix);
  // Cool aura behind the item and crisp icy rim specular catch.
  art += back * mask * vec3(0.24, 0.66, 0.92) * 0.55;
  art += (rim + halo) * vec3(0.50, 0.90, 1.0) * (0.12 + 0.34 * light);
  // Prominent bobbing ice crystal shards positioned from the card's crystal seed.
  float bob = (sin(seconds * 0.65) + 1.0) * 0.04875 - 0.04875;
  vec2 seed = vec2(_AbsorptionMaskMax * 65.0, _AbsorptionMaskMax * 91.0 + bob);
  vec2 shardUV = (uv + seed - 0.5) * vec2(aspect, 1.0);
  shardUV = shardUV * (_IceCrystalsScale + 0.35) + vec2(_CrystalUOffset, _CrystalVOffset) + 0.5;
  vec4 shards = texture(_IcyShards, shardUV);
  vec3 shardColor = shards.rgb * 1.18 + light * vec3(0.55, 0.85, 1.0);
  art = mix(art, shardColor, clamp(shards.a * 1.25, 0.0, 1.0));
  // Crisp drifting glacial star glints.
  vec2 snowUV = cardUV(uv) + vec2(-0.025, -0.04) * seconds;
  float stars = sparks(snowUV * 5.8, 0.017, true)
              + sparks((cardUV(uv) + vec2(0.018, -0.065) * seconds) * 11.5, 0.010, false) * 0.85;
  art += (0.20 + 0.80 * back) * stars * vec3(0.62, 0.94, 1.0) * 1.25;
  return art;
}`;
