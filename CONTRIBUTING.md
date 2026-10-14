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

- `main` is protected in GitHub. Do not push directly to `main`; use a feature branch and pull request.
- GitHub branch-protection settings determine which checks and review requirements are enforced automatically. If you need to verify or change them, check the repository's **Settings → Branches** page.
- Create a feature branch for each change, for example `ORION-13-repo-setup`.
- Open a pull request into `main` using the PR template.

## Merge expectations

The following are the team's intended review practices. GitHub enforces a requirement only when it is enabled in the repository settings.

- Request at least one approving review before merging when another team member is available.
- The PR author should not approve their own PR.
- Resolve review conversations before merging.
- Once CI is configured (ORION-16), required checks must pass. Do not configure required checks that do not yet exist.
- Update the feature branch with the latest `main` before merging when needed to resolve conflicts or satisfy branch-protection rules.
- Use squash-merge, which is the repository's intended default.
