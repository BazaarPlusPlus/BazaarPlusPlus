# Fonts

`LXGWWenKai-Regular.ttf` is a subset of LXGW WenKai Regular from the
Homebrew `font-lxgw-wenkai` cask. It keeps Latin, common UI symbols, CJK
punctuation, fullwidth forms, and the main CJK Unified Ideographs block so
BPP-owned Chinese UI text still has broad coverage without embedding the full
upstream font.

Regenerate with fontTools from a locally installed cask font:

```sh
python3 -m fontTools.subset "$HOME/Library/Fonts/LXGWWenKai-Regular.ttf" \
  --output-file=src/BazaarPlusPlus/Resources/Fonts/LXGWWenKai-Regular.ttf \
  --unicodes='U+0020-007E,U+00A0-00FF,U+0100-017F,U+2000-206F,U+20A0-20CF,U+2100-214F,U+2190-21FF,U+25A0-25FF,U+2600-26FF,U+2E80-2EFF,U+2F00-2FDF,U+2FF0-2FFF,U+3000-303F,U+3100-312F,U+31A0-31BF,U+31C0-31EF,U+3200-32FF,U+3300-33FF,U+4E00-9FFF,U+FE30-FE4F,U+FF00-FFEF' \
  --layout-features='*' --name-IDs='*' --name-languages='*'
```
