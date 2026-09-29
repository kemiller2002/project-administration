# Project Knowledge Hub

A docs-as-code home for information that reaches across projects: FAQs, how-tos,
guided procedures, reference material, and project overviews.

## What belongs here

Put content here when it:

- helps someone work in more than one project;
- explains how projects connect;
- provides a common entry point and links to project-owned detail; or
- answers a recurring question whose answer spans project boundaries.

Keep implementation-specific API and code documentation with the project that
owns it. This hub should link to that source rather than duplicate it.

## Quick start

```sh
npm ci                                   # pinned Forma, Folio and Limen
python3 scripts/knowledge.py validate
python3 scripts/knowledge.py build
python3 -m http.server 8000 --directory dist
```

Then open <http://localhost:8000>.

## Add content

1. Choose a type in `content/`: `projects`, `how-tos`, `guides`, `faqs`, or
   `reference`.
2. Copy the matching file from `templates/`.
3. Give the file a permanent, descriptive kebab-case name.
4. Complete the YAML front matter. Every page needs an owner, review date,
   audience, and project tags.
5. Run `python3 scripts/knowledge.py validate`.
6. Open a pull request and request review from the named owner.

See [the authoring guide](content/reference/authoring-guide.md) and
[the architecture decision](docs/architecture.md) for the complete model.

## Repository map

```text
content/                 Published knowledge, grouped by reader intent
templates/               Copyable authoring templates
docs/                    Architecture and governance
scripts/knowledge.py     Validation and static-site generation
site/templates/          Page shell (Forma patterns, Folio print structure)
site/engine/             Limen engine: search state and projection (no browser access)
site/kernel/             Limen wiring: the only browser-facing script
site/style.css           Portal layout on Forma tokens
tests/site/              Engine unit tests and Chromium portal checks
dist/                    Generated output (not committed)
```

## Content lifecycle

`draft` → `published` → `archived`

Published pages whose `review_by` date has passed still build, but validation
reports them as stale. Archived pages remain at their stable URL and direct
readers to a replacement.

## Echelon installation inventory

This repository is also the canonical **installation inventory** for Echelon
systems: which system is installed where, at which version, from which
release, with what evidence. History lives in `installations/events/` and the
generated current state in `generated/installations-current.json`. The
knowledge-hub content above is unaffected. See
[`docs/installation-inventory.md`](docs/installation-inventory.md).

## Echelon application foundations

`.echelon/foundations.json` declares the six shared Echelon capabilities this
application must consume, and the `echelon-foundations` workflow verifies each
one is installed, pinned, used and evidenced:

| Capability | Pinned | Where it is used |
|---|---|---|
| Aegis | `EchelonFoundry.Aegis.Core` 1.0.0 | Unexpected-failure capture in the `administration` CLI; boundaries declared in `aegis-boundaries.json` |
| Forma | `@echelon-foundry/design-system` 0.2.0 | Portal presentation: tokens, `ef-search`, `ef-empty-state`, `ef-surface` |
| Folio | `@echelon-foundry/print-components` 0.3.0 (`273b18f`) | Printed pages: `ef-print-document`/`-section`/`-header`/`-footer`/`-code`/`-table` and `print.css` |
| Limen | `@echelon-foundry/typescript-wasm-kernel` 0.6.2 | Portal search: `site/engine/` owns state, `site/kernel/` is the only browser code; boundary in `limen.config.json` |
| Ordo | `@echelon-foundry/sde` 1.3.0 | `.sde/`, `.echelon/sde.json` |
| Praxis | `@echelon-foundry/repository-operating-system` 3.1.4 | `./ros` governance; `.echelon/ros.json` |

The site build copies the pinned packages' files from `node_modules` into
`dist/assets/@echelon-foundry/`; nothing from them is copied into source.

### Keeping Limen current

Limen's generated `limen-verify.yml` runs the newest published Limen, while
this repository pins an exact version. `.github/workflows/limen-upgrade.yml`
checks npm daily (or on demand, optionally for a named version) and, when a
newer Limen exists, runs `scripts/limen-upgrade.sh`:
1. installs the new version exact-pinned;
2. applies `limen upgrade` and updates `.echelon/foundations.json`;
3. verifies the result (strict Limen check, search engine tests, site build and
   Chromium portal checks);
4. records a `mechanical` ROS work item and opens a PR for review.

Nothing merges automatically. PRs opened with the default `GITHUB_TOKEN` don't
trigger the other workflows. Add a `LIMEN_UPGRADE_TOKEN` secret (contents and
pull-requests write) if the usual checks should run on those PRs as well. With
the default token, enable **Settings → Actions → General → Allow GitHub Actions
to create and approve pull requests**.
