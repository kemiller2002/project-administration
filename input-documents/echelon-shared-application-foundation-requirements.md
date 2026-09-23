# Project Administration — Echelon Shared Application Foundation Requirements

Status: **Required**

These requirements govern every Project Administration feature when applicable.

## Aegis

Project Administration MUST use Aegis for unexpected operational failure at GitHub/repository, network, storage, filesystem, external integration, browser/WASM interop, import/export, publishing, and other architectural boundaries. Expected domain outcomes remain typed Project Administration/Ordo outcomes. Raw external exceptions MUST be translated at their owning boundaries, sensitive context MUST be redacted, and recovery MUST respect authorization, idempotency, verification, and unknown-effect handling.

## Forma

Every interactive browser UI surface MUST consume a pinned Forma release and use existing Forma patterns/components/tokens before local equivalents. Forma presentation MUST not be copied or forked locally. Native HTML owns semantics, Limen owns browser interaction beyond native behavior, and Project Administration/Ordo owns domain state and legal transitions. Mobile, keyboard, responsive, and accessibility requirements inherit Forma's contracts.

Aegis fault presentation visible to a user MUST use applicable Forma fault/error components.

## Folio

Any printable, PDF, paginated, print-preview, project report, status report, work summary, handoff document, or other paper-oriented artifact MUST consume a pinned Folio release and use existing Folio document primitives before local print implementations. Application data/meaning remains Project Administration-owned; Folio owns reusable print/layout intent; the renderer owns physical pagination.

Folio is conditional until such an artifact exists. The first requirement introducing one automatically activates the Folio dependency.

## Verification

Shared dependencies MUST be pinned to released versions or immutable artifacts. Completion evidence MUST demonstrate actual shared-capability use, relevant Aegis boundary tests, Forma browser/mobile/accessibility tests, Folio print/PDF tests where applicable, and an explicit decision record for any exception.
