set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '..'

# Regenerate the installer's tokens.css, the mod's InGameTheme.g.cs, and the Android App resources from design/tokens.json and design/assets/.
build:
    bun design/build.ts

# Check that generated tokens, the shared stylesheet, the site's hero colours and the mod's supporter colours agree with design/tokens.json.
check:
    bun design/build.ts --check
