set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '..'

# Regenerate the installer's tokens.css, the mod's InGameTheme.g.cs and InGameSizes.g.cs, and the Android App resources from design/tokens.json and design/assets/.
build:
    bun design/build.ts

# Draw the Desktop Kit's plate PNGs into the mod from design/components/InGameKit/art/material.png.
kit-art:
    bun design/kit-art.ts

# Check that generated tokens, the shared stylesheet, the site's hero colours and the mod's supporter colours agree with design/tokens.json, and that the Desktop Kit's plates match design/kit-art.ts.
check:
    bun design/build.ts --check
