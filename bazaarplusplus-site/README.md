# BazaarPlusPlus Site

The public website for BazaarPlusPlus: product information, installer downloads, Hero Analysis, and project support. The site deploys independently of the mod and installer Product Release.

Develop from a full monorepo checkout: the site consumes shared release modules and support artwork outside this project directory. The ownership boundaries and data contracts are recorded in [Architecture](docs/ARCHITECTURE.md).

Start with the [workspace development guide](../docs/development.md) for setup and verification. Use [CONTEXT.md](CONTEXT.md) for Hero Analysis terminology and [AGENTS.md](AGENTS.md) for contribution rules. Architectural rationale lives in [the decision records](docs/adr/).

## Deploy

`.github/workflows/deploy-site.yml` owns the deploy commands and triggers. A push to `master` that touches the site, the shared release modules, or the workflow deploys the preview Worker. Production deploys only from a manual run of that workflow, and the `site-production` GitHub Environment holds the run until a reviewer approves it. Both environments hold the Cloudflare credentials and accept deploys from `master` only; nobody runs `wrangler deploy` with personal credentials for a shared deploy.

The download page reads the Platform Release Manifests described in [Architecture](docs/ARCHITECTURE.md). Publish a complete manifest before a production deploy that depends on it; the preview deploy does not change that order.
