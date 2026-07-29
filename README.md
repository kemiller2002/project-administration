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
site/                    Portal shell and styles
dist/                    Generated output (not committed)
```

## Content lifecycle

`draft` → `published` → `archived`

Published pages whose `review_by` date has passed still build, but validation
reports them as stale. Archived pages remain at their stable URL and direct
readers to a replacement.
