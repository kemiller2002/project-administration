---
id: ref-authoring-guide
title: Knowledge authoring reference
summary: Metadata, structure, and quality rules for hub contributors.
type: reference
status: published
owner: knowledge-hub-maintainers
projects: [knowledge-hub]
audience: [contributors]
tags: [standards, authoring]
updated: 2026-07-27
review_by: 2027-07-27
related: [howto-add-knowledge, faq-where-content-lives]
---

# Knowledge authoring reference

## Required metadata

| Field | Rule |
|---|---|
| `id` | Permanent, unique, lowercase kebab-case |
| `title` | Reader language; questions for FAQs, verbs for tasks |
| `summary` | One sentence that distinguishes the page in search |
| `type` | `project`, `how-to`, `guide`, `faq`, or `reference` |
| `status` | `draft`, `published`, or `archived` |
| `owner` | Accountable team or role, not an individual's name |
| `projects` | Lowercase project keys; use `all` only when truly global |
| `audience` | Reader roles; `all` is allowed |
| `tags` | A small set of secondary discovery terms |
| `updated` | Last factual review, in `YYYY-MM-DD` format |
| `review_by` | Next required review, in `YYYY-MM-DD` format |
| `related` | IDs of directly useful hub pages |

## Content tests

Before review, ask:

1. Can a reader determine applicability from the title and summary?
2. Does each step describe an observable action?
3. Are handoffs between projects explicit?
4. Is there a success check?
5. Are failures safe and is escalation ownership clear?
6. Are project facts linked rather than copied?
7. Would a searcher use the terms in the title, summary, and headings?

## Sensitive information

Never publish secrets, tokens, personal data, customer data, private keys, or
instructions that bypass access controls. Link to an approved secrets manager
or restricted system and describe the access request at a safe level.
