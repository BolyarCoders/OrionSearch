# OrionSearch design review and direction

Scope: frontend only, existing feature/frontend branch. Web research library for people collecting sources and running topic research. Apple HIG principles are translated to web; this is not a macOS skin.

## Review

Critical: none identified in the inspected screens.

High — Accessibility: old muted text #737B8C on #111520 measures 4.29:1; small violet text #7056F5 measures 3.77:1. Both fall below 4.5:1. `accessibility.md › Vision`: “Strive to meet color contrast minimum standards.” Replace these semantic tokens, increase small utility text, and preserve visible keyboard focus.

High — Hierarchy and craft: decorative space covers, orbital hero and four colorful metric cards compete with archive titles. Replace with text-led dossier cards, a quiet count strip and one creation action. `layout.md › Visual hierarchy`: “Order content by relative importance.” Choosing a folder silhouette as the product signature is studio judgment, not an Apple requirement.

Medium — Platform and interaction: duplicate Home/Archives destinations, disabled future navigation, a notification placeholder and a nonfunctional avatar imply unavailable capabilities. Present real destinations and contextual search. Add Escape/focus handling to the mobile drawer.

Medium — Typography and content: many essential labels are 10–12px. Use one system family and a readable utility scale. `typography.md › Ensuring legibility`: “Use font sizes that most people can read easily.” Keep validation, permissions and API errors intact.

Medium — Brand: violet is repeated across headings, chips and controls. Reserve it for selection and the principal action. `branding.md › Best practices`: “Apply your app’s accent color judiciously.”

Low — Polish: static demo status should not pulse; remove decorative scaling and respect reduced motion. System light/dark appearance should work without a forced dark class.

## Token plan (before implementation)

| Role | Light | Dark |
|---|---|---|
| Canvas | #F4F5F7 | #151719 |
| Surface | #FFFFFF | #1C1F22 |
| Primary text | #1C2433 | #E8EBEF |
| Secondary / muted | #566174 / #626D7D | #ACB4C0 / #9AA4B2 |
| Accent | #5746AF | #B6A5F5 |
| Divider / control boundary | #DCE1E7 / #818B98 | #353B43 / #727E8D |

Calculated normal text contrast on surface: light primary 15.56, secondary 6.25, muted 5.25, accent 7.24; dark primary 13.84, secondary 7.92, muted 6.56, accent 7.64. Primary button uses a separate on-accent color in each appearance. Semantic success/warning/error remain distinct and always have text labels.

Type: system sans; page title 34/40, section 22/28, card title 18/26, body 15/24, utility 13/20. Spacing: 4, 8, 12, 16, 24, 32, 40px. Radii: controls 6px, panels 8px. Desktop controls at least 36px, touch controls 44px. No decorative animation, no raster assets needed.

Regular layout:

    [OrionSearch | Research library                      Demo v]
    [ Archives  | Archives                         + New archive]
    [           | description                                   ]
    [           | matching / active / paused / indexed sources   ]
    [           | Search       All Active Paused ... Sort View  ]
    [           | _folder_       _folder_       _folder_          ]
    [           | Title          Title          Title            ]
    [           | Description    Description    Description      ]
    [ Settings  | Sources/status · updated / pagination           ]

Compact layout:

    [Menu   Library                              Demo v]
    [Archives                                          ]
    [description                       + New archive   ]
    [counts, wrapping                                  ]
    [Search                                            ]
    [filters, wrapping / sort / view                    ]
    [_folder_                                          ]
    [Title / description / sources / status / updated   ]

Plan critique: a neutral dashboard alone would be generic. The folder tab, source-led metadata and research vocabulary make the archive itself the identifying object. No arbitrary numbering, fake charts, generic AI imagery, decorative gradients or marketing slogans. Keep the existing research and source workflows as the acceptance surface. This direction supersedes the earlier cosmic artwork specification.

Implementation order: contrast/focus/targets; honest navigation and mobile drawer; dossier composition across library/detail/create; consistent typography and appearance; rendered desktop/mobile/light/dark verification and workflow regression tests.

## Rendered review

Compared the implemented screens with the wireframes and token plan above, using actual browser output in `apps/web/qa/design-*.png`.

| Dimension | Result |
|---|---|
| Composition | Library heading, count strip, grouped controls, dossier collection and pagination follow the plan. Detail and creation share the same surfaces and hierarchy. |
| Hierarchy | Titles and the primary action lead; decorative covers and competing metric boxes are removed. |
| Density | Three archive columns at 1440px; cards reflow according to available width and text size. Creation uses a compact current-step indicator on mobile. |
| Spacing | 4/8/12/16/24/32/40 rhythm; related card metadata stays grouped. |
| Proportions | Folder tab is a small identifying detail rather than a large illustration; form gets more width than its preview. |
| Typography | One system family; utility text enlarged to 13px; card descriptions 14px. A 200% text check exposed footer overflow, fixed by wrapping metadata and using an adaptive grid. |
| Emphasis | Violet selects and initiates; semantic statuses retain written labels. Both system appearances inspected. |
| Empty/loading/error | Search empty state, loading placeholders and explicit 401 fixture rendered. Error and trace information retained without seeded live data. |
| Ergonomics | 320px check exposed undersized primary buttons; corrected to 44px mobile button targets. Search shortcut, drawer focus trap, Escape and focus restoration exercised. |

The existing Orion orbit motif is retained as a flat SVG mark. Existing artwork files remain available, but are no longer used as archive decoration. Human preference approval of this new direction is pending; the screenshots provide a concrete result to review.

Browser verification (local demo data except explicit HTTP fixture): five-step creation with dates/domains/languages/config, lifecycle pause/activate/archive/restore, source filters, settings save, research completion, demo connection check, loading and authentication errors. Render checks: 1440/768/390/320px, light/dark, reduced-motion preference, 200% root text size. No browser exceptions were observed in the successful runs. Initial development runs hit route-compilation timeouts; reruns used compiled routes. The controlled error fixture releases all Strict Mode requests.

This is a web frontend review, not native macOS/iOS certification, a complete screen-reader audit, or authenticated backend acceptance.

## Verification snapshot — 2026-10-27

Scope: local uncommitted frontend, branch `feature/frontend`, unchanged HEAD `10b8dc1bbb29e6b4d2d9eda4e306c97abc8f030c`. No backend, API client, contract types or test assertions changed. No branch, commit, push or deployment created.

| Gate | Result | Evidence |
|---|---|---|
| Frontend suite | PASS | `npm --prefix apps/web test -- --maxWorkers=1 --testTimeout=15000`: 58 tests, 6 suites. Initial 5-second timeout under concurrent machine load passed on sequential rerun. |
| TypeScript | PASS | `npm --prefix apps/web run typecheck`, followed by final Next build type validation. |
| Production artifact | PASS | Final `npm --prefix apps/web run build` exits 0; all seven static pages generated and dynamic detail route packaged. |
| Production render/keyboard smoke | PASS | `next start --port 3000`: library/detail/sources/create, light/dark, 1440/768/390/320px, 200% text, focus/keyboard and mobile button sizes. No document overflow or page exceptions in successful checks. |
| Production loading/error fixture | PASS | Delayed request + explicit 401 fixture with trace ID, no demo leakage, mobile error layout. |
| Workflow smoke | PASS | Development browser: five-step create/config, lifecycle restore, source filters, settings, research completion and connection check. |
| Separate ESLint/format gate | Not configured | No separate lint/format script in this frontend; no lint-clean claim. |
| Human approval of direction | UNVERIFIED | Ready for the user to review the rendered design. |
| Authenticated backend/worker acceptance | UNVERIFIED | Outside this frontend design task. Demo workflows and explicit browser fixtures are not provider acceptance. |

Current evidence: [light library](../qa/design-final-light.png), [dark library](../qa/design-final-dark.png), [mobile library](../qa/design-final-mobile.png), [archive](../qa/design-detail.png), [create](../qa/design-create-layout.png), [mobile create](../qa/design-create-320.png), [sources at 320px](../qa/design-sources-320.png), [200% text](../qa/design-text-200.png), [loading](../qa/design-loading.png), [401 fixture](../qa/design-live-error.png).
