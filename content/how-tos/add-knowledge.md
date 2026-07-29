---
id: howto-add-knowledge
title: Add or update knowledge
summary: Publish a maintainable FAQ, how-to, guide, reference, or project overview.
type: how-to
status: published
owner: knowledge-hub-maintainers
projects: [knowledge-hub]
audience: [contributors]
tags: [authoring, publishing]
updated: 2026-07-27
review_by: 2027-01-27
related: [ref-authoring-guide, faq-where-content-lives]
---

# Add or update knowledge

## Before you begin

- Search the hub for an existing canonical page.
- Identify the team that will own correctness after publication.
- Collect links to the source material in affected projects.

## Steps

1. Choose the content type that matches the reader's intent.
2. Copy the corresponding file from `templates/` into the matching `content/`
   directory.
3. Replace the template metadata, using permanent IDs and real ownership.
4. Write the shortest path to the reader's outcome.
5. Link project-specific details to their canonical sources.
6. Run `python3 scripts/knowledge.py validate`.
7. Run `python3 scripts/knowledge.py build` and inspect the generated portal.
8. Request review from the content owner and every affected project owner.

## Verify the result

Validation completes without errors, the page appears under its content type,
filters include the expected projects and audience, and all related pages open.

## If it does not work

Read the validation message for the file and field. If the taxonomy cannot
express the content without inventing misleading metadata, ask the hub
maintainers to evolve the model before publishing.
