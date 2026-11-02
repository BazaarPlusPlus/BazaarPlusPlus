set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '..'

# Regenerate the installer's tokens.css, the mod's InGameTheme.g.cs, and the Android and iOS App resources from design/tokens.json.
build:
    node design/build.mjs

# Check that generated tokens, the shared stylesheet and the site's hero colours agree with design/tokens.json.
check:
    node design/build.mjs --check
