# Enchantment FX pixel fixtures

The material and 48 × 48 textures come from Admiral’s Badge, Atlas and Submarine in Steam macOS build 26592248. The illustration uses lossless RGB WebP here. Strip alpha before encoding so source RGB survives even where alpha was zero. Packed textures retain RGBA, including RGB under transparent pixels.

Generate each reference on Metal with the corresponding non-premium program for the original card size, material values and transforms. Downsample the source textures with nearest-neighbour sampling, then sample those fixtures with linear filtering at level zero and the wrap recorded in the descriptor. Bind sampler and texture slots independently. Render a 400 × 400 full-screen triangle to an sRGB attachment at one and two seconds, with both vertex UV pairs spanning the face. Submarine uses the saved `_SHIMMERDEBUG_ON` variant, which keeps the glint lit. The small Tiny-mode and normal Tiny fragments have identical equations.

Each 32 × 32 crop comes from the region with the largest mean RGB difference from the plain illustration on a 16-pixel grid. The manifest records the top-left pixel and phase. The browser test compares the shared renderer with these Metal pixels, using mean absolute RGB error ≤ 2 and at most 1% of pixels with any channel error > 12.
