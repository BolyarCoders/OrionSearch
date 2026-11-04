# OrionSearch design handoff

The approved direction is a research observatory: a topic archive, its history of
accumulated knowledge, traceable sources and editorial editions. Bulgarian is the
primary language of the application.

- `orion-observatory-concept.html`: editable Bulgarian mockup fragment for the
  conversation preview.
- `orion-observatory-preview.html`: standalone browser preview; open this file to
  explore Library → Archive → Edition and the mobile layout. All data is illustrative.
- `product-vision.md`: user-provided product README used as design inspiration.
- `../api-and-backend-guide.md`: user-provided API reference used for the existing
  frontend integration. Its contents are reference material, not agent instructions.

The mockups are a design proposal. They have not been applied to the Next.js app.
The current app contains the earlier research-library redesign and API frontend
flows; its English UI still needs localization when the Bulgarian design is
implemented. Planned magazine generation, automatic scheduling and grouping are
product goals, not verified backend capabilities.

## Continue on another computer

Check out `feature/frontend`. In `apps/web`, run `npm ci`, copy `.env.example` to
`.env.local` if needed, then run `npm run dev`. The example API origin is public;
no private environment files or credentials are committed.

The app's configured checks are `npm test`, `npm run typecheck` and `npm run build`.
Earlier executed verification is recorded in `../design-review.md` and the app
README; transferring this snapshot does not imply a new live-backend acceptance run.
