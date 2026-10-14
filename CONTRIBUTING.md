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

- `main` is protected. Do not push directly to it.
- Create a feature branch for each change, for example `ORION-13-repo-setup`.
- Open a pull request into `main` using the PR template.

## Merge rules

- At least 1 approving review is required before merging.
- The PR author cannot approve their own PR.
- All review conversations must be resolved.
- Required CI checks must pass once they exist (see ORION-16).
- Branches must be up to date with `main` before merging.
- Squash-merge is the default merge method.
