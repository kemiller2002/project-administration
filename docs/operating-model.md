# Operating model

## Publishing workflow

1. **Discover:** search before creating; update an existing canonical page when
   the reader intent is the same.
2. **Place:** choose the content type based on the reader's question.
3. **Author:** start from a template and link to project-owned evidence.
4. **Review:** obtain approval from the content owner and affected project
   owners.
5. **Validate:** run the local validator; CI should run the same command.
6. **Publish:** merge to the default branch and deploy the generated `dist/`.
7. **Maintain:** review by the declared date; archive or replace stale material.

## Definition of done

A page is ready when:

- a reader can tell whether it applies to them;
- prerequisites and permissions are explicit;
- project boundaries and handoffs are named;
- procedures include a verifiable outcome and recovery path;
- links point to canonical project sources;
- no credentials, tokens, customer data, or private operational data appear;
- the owner and next review date are real commitments; and
- validation succeeds.

## Editorial rules

- Write task titles as verbs: “Request production access.”
- Start FAQs with the answer, not background.
- Use one numbered action per step.
- Name the UI, command, repository, or team precisely.
- Explain why only where it affects a decision.
- Include expected results after risky or ambiguous actions.
- Prefer a link over copied source material.
- Define acronyms on first use.

## Triage

When feedback arrives:

| Signal | Action |
|---|---|
| Incorrect or unsafe instruction | Unpublish or correct immediately |
| Broken external link | Replace with the canonical source |
| Missing project step | Assign affected project owner and update journey |
| Duplicate page | Choose one canonical page; archive and link the other |
| Stale review date | Owner reviews, revises, archives, or delegates |
| Search miss | Improve title, summary, tags, or terminology |
