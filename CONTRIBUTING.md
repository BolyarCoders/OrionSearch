# Contributing to OrionSearch

## Repository layout

This is a monorepo.

```
apps/
  web/                  Next.js web application
services/
  api/                  FastAPI backend
  research-worker/      AI agent workflow and research tools
packages/
  contracts/            Shared OpenAPI / JSON contracts
infra/
  docker/               Infrastructure configuration
docs/                   Product and technical documentation
compose.yaml            Local stack (planned, ORION-14)
```

The layout follows the project README and may change as the team builds the system.

## Branches and pull requests

- `main` should be protected in GitHub. Verify that protection is enabled before merging; the current repository settings were found to have it disabled.
- Do not push directly to `main` once protection is enabled.
- Create a feature branch for each change, for example `ORION-13-repo-setup`.
- Open a pull request into `main` using the PR template.

## Merge rules

- At least 1 approving review is required before merging.
- The PR author cannot approve their own PR.
- All review conversations must be resolved.
- Required CI checks must pass once they exist (see ORION-16); do not require checks that have not been configured yet.
- Branches should be up to date with `main` before merging.
- Squash-merge is the default merge method.
