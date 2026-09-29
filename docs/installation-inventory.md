# Echelon installation inventory

Project Administration is the canonical owner of **what Echelon system is
installed where**. It answers, for any target:

- which Echelon systems are installed, at which version, and where;
- which release/artifact produced each installation;
- when it was installed, upgraded, verified or removed;
- whether it is currently installed, removed, stale or indeterminate;
- what evidence proves the registration, and which execution performed it.

## Responsibilities

| System | Owns |
|---|---|
| Echelon Registry | what systems exist and what capabilities they provide (catalog) |
| **Project Administration** | **what is installed where (installation history + current state)** |
| Ordo | what execution states, capabilities, receipts and transitions mean |
| Praxis | execution, orchestration, provenance; the registration client |
| Conditor | installation, bootstrap, upgrade and uninstall |

Project Administration is **not** the store for repository-local work
execution: Praxis work items, executions, telemetry and checkpoints stay in
each repository's `.ros/`. The Echelon Registry never holds installation
instances, and this repository never becomes a catalog.

## Layout (owned by Project Administration)

```
installations/events/<event-id>.json   canonical history, append-only, one file per event
generated/installations-current.json   generated current-state projection (NOT canonical)
schemas/installation-event.v1.schema.json
schemas/installations-current.v1.schema.json
```

Only the `administration` executable reads or writes this layout. Spoke
systems never do; they call the capability boundary below.

- **Append-only.** An event is written with create-new semantics and never
  edited. An upgrade from 1.4.0 to 1.5.0 adds an `upgraded` event and keeps
  the 1.4.0 `installed` event. Removal adds a `removed` event; history is
  never deleted.
- **Concurrency-safe.** One file per event means concurrent registrations
  never overwrite each other and merge in Git without conflict. Within a
  checkout a lock serializes decide → write → project. The projection is a
  deterministic function of history, so after any merge it is regenerated
  (`administration installation project`), and CI refuses a stale one
  (`project --check`).
- **Idempotent.** `operationId` is the idempotency key: a retried operation
  replays the existing event. A registration identical to the current state
  (same system, version and artifact digest on the same target) records
  nothing and returns `unchanged`. The same version with a different artifact
  digest is refused (`artifact-conflict`): versions are immutable.

Inventory events and the generated projection are listed in `ros.json`
`ignoredPaths`: they are data recorded through the capability, and each
event carries its own actor/execution provenance.

## Targets and privacy

| Target kind | Identity |
|---|---|
| `repository` | stable repository identity `owner/repository` |
| `environment` | an explicitly configured logical ID (`[a-z0-9][a-z0-9._-]{0,63}`), e.g. `ws-primary`, `ci-ubuntu` |

Nothing infers a workstation identity. Hostnames, usernames, home
directories, full local paths, hardware serials and MAC addresses are never
collected, and the validator refuses values shaped like local paths, emails
or MAC addresses in any field (`privacy-refused`).

## Capability boundary

The receiving-system executable is `administration`. Its typed contract is
`echelon.installation.request/v1` → `echelon.installation.result/v1`
(contracts `installation.register`, `installation.query`,
`installation.remove` in the Echelon Registry; `installation.verify` and
`installation.reconcile` are additional operations of the same contract).

```
administration installation register  --request request.json --json
administration installation remove    --request request.json --json
administration installation reconcile --request request.json --json   # observedState: installed|removed|indeterminate
administration installation verify    --request request.json --json
administration installation query     [--target KIND:ID] [--system ID] [--version V] [--older-than V] [--state S] [--stale-after-days N] --json
administration installation history   --target KIND:ID [--system ID] --json
administration installation project   [--check]
```

Result `status` is one of `recorded`, `replayed`, `unchanged`, `refused`
(exit 3), `invalid` (exit 2) or `failed` (exit 4).

When `--catalog <echelon-registry registry/systems.json>` is supplied, an
unknown system is refused. Without a catalog the request is still recorded:
Echelon Registry absence must not block installation history.

### Transports

- **Local executable** — the caller runs `administration` against a
  Project Administration checkout (`--store`). Praxis's
  `praxis installation register` uses this when `.echelon/administration.json`
  names a local store.
- **GitHub workflow** — `.github/workflows/installation-request.yml` accepts
  the same request document as a `workflow_dispatch` input, runs the same
  executable, and commits the event. Praxis dispatches it with
  `gh workflow run installation-request.yml -R kemiller2002/project-administration -f request=...`.

The transport is replaceable; the typed request contract is the boundary.

## Queries

```
# All systems installed in repository X
administration installation query --target repository:echelon-foundry/example
# All targets running Ordo 1.4.0
administration installation query --system ordo --version 1.4.0 --state installed
# Targets with an older Praxis
administration installation query --system praxis --older-than 3.6.0
# Current Conditor on target Y
administration installation query --target environment:ws-y --system conditor
# Upgrade history for target Y
administration installation history --target environment:ws-y
# Removed systems / indeterminate records
administration installation query --state removed
administration installation query --state indeterminate
# Stale installations (not observed in 30 days)
administration installation query --stale-after-days 30 --stale-only
```

Example output:

```
TARGET: repository:echelon-foundry/example
SYSTEM                  VERSION       STATE
ordo                    1.4.0         installed
praxis                  3.6.0         installed
```

Query semantics live in `Administration.Installations.Queries`; a future
Forma UI consumes the same functions/JSON and owns no rules.
