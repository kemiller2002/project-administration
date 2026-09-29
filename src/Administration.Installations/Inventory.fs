/// The pure core: given canonical history and a validated request, decide
/// what (if anything) to record; given history, compute the current state.
///
/// Nothing here reads a clock or a file. Replaying the same history always
/// produces the same projection, which is what makes the generated
/// projection safe to regenerate after concurrent writers.
module Administration.Installations.Inventory

open System
open System.Security.Cryptography
open System.Text
open Administration.Installations.Model

/// What a request resolved to.
type Decision =
    /// Record this new event.
    | Record of InstallationEvent
    /// An event with this operationId already exists: return it, record
    /// nothing (retry safety).
    | Replay of InstallationEvent
    /// The request describes exactly the current state: record nothing, so
    /// a repeated registration cannot create misleading duplicate state.
    | Unchanged of CurrentInstallation
    /// The request contradicts recorded state in a way that needs an
    /// explicit reconciliation rather than a silent overwrite.
    | Refused of code: string * message: string

/// Canonical ordering of history: by occurrence, then by event id.
let order (events: InstallationEvent list) =
    events |> List.sortBy (fun e -> e.OccurredAt.UtcTicks, e.EventId)

let private keyOf (e: InstallationEvent) = Target.key e.Target, SystemId.value e.SystemId

/// Deterministic event id: occurrence time plus a hash of the operation id.
/// The same operation always maps to the same file; different operations
/// never collide in practice.
let eventIdFor (operationId: string) (occurredAt: DateTimeOffset) =
    let hash = SHA256.HashData(Encoding.UTF8.GetBytes operationId)
    let stamp = occurredAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'")
    $"IE-{stamp}-{Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 16)}"

let private fold (acc: CurrentInstallation option) (e: InstallationEvent) : CurrentInstallation option =
    match acc with
    | None ->
        Some
            { Target = e.Target
              SystemId = e.SystemId
              State = e.ResultingState
              Version = e.SystemVersion
              PreviousVersions = []
              FirstInstalledAt = e.OccurredAt
              LastChangedAt = e.OccurredAt
              LastObservedAt = e.OccurredAt
              Source = e.Source
              Execution = e.Execution
              LastEventId = e.EventId
              EventCount = 1 }
    | Some c ->
        let versionChanged = SemVer.compareVersions c.Version e.SystemVersion <> 0 || c.Version.Text <> e.SystemVersion.Text

        let previous =
            if versionChanged && not (c.PreviousVersions |> List.exists (fun v -> v.Text = c.Version.Text)) then
                c.PreviousVersions @ [ c.Version ]
            else
                c.PreviousVersions

        let changed = versionChanged || c.State <> e.ResultingState

        Some
            { c with
                State = e.ResultingState
                Version = e.SystemVersion
                PreviousVersions = previous
                LastChangedAt = (if changed then e.OccurredAt else c.LastChangedAt)
                LastObservedAt = e.OccurredAt
                // A removal keeps the source that produced the installation.
                Source = (if e.Operation = Removed then c.Source else e.Source)
                Execution = (if e.Execution.IsSome then e.Execution else c.Execution)
                LastEventId = e.EventId
                EventCount = c.EventCount + 1 }

/// The current state of every (target, system) pair, in a stable order.
let project (events: InstallationEvent list) : CurrentInstallation list =
    order events
    |> List.groupBy keyOf
    |> List.choose (fun (_, history) -> history |> List.fold fold None)
    |> List.sortBy (fun c -> Target.key c.Target, SystemId.value c.SystemId)

/// The current state of one pair.
let currentFor (target: Target) (system: SystemId) (events: InstallationEvent list) =
    events
    |> List.filter (fun e -> e.Target = target && e.SystemId = system)
    |> order
    |> List.fold fold None

let private sameArtifact (a: InstallationSource) (b: InstallationSource) =
    match a.Digest, b.Digest with
    | Some x, Some y -> String.Equals(x, y, StringComparison.OrdinalIgnoreCase)
    | _ -> true

let private toEvent (r: InstallationRequest) operation previous state =
    { SchemaVersion = EventSchemaVersion
      EventId = eventIdFor r.OperationId r.OccurredAt
      OperationId = r.OperationId
      CorrelationId = r.CorrelationId
      OccurredAt = r.OccurredAt
      Operation = operation
      SystemId = r.SystemId
      SystemVersion = r.SystemVersion
      PreviousVersion = previous
      Target = r.Target
      Source = r.Source
      Evidence = r.Evidence
      Actor = r.Actor
      Execution = r.Execution
      ResultingState = state }

/// Decide what a validated request means against canonical history.
let decide (events: InstallationEvent list) (r: InstallationRequest) : Decision =
    match events |> List.tryFind (fun e -> e.OperationId = r.OperationId) with
    | Some existing -> Replay existing
    | None ->
        let current = currentFor r.Target r.SystemId events
        let sameVersion (c: CurrentInstallation) = c.Version.Text = r.SystemVersion.Text

        match r.Capability, current with
        // -------------------------------------------------------- register
        | Register, None -> Record(toEvent r Installed None Present)
        | Register, Some c when c.State <> Present -> Record(toEvent r Installed None Present)
        | Register, Some c when sameVersion c && sameArtifact c.Source r.Source -> Unchanged c
        | Register, Some c when sameVersion c ->
            Refused(
                "artifact-conflict",
                $"{SystemId.value r.SystemId} {r.SystemVersion.Text} is already recorded on {Target.key r.Target} with a different artifact digest; versions are immutable, so record what is actually installed with installation.reconcile"
            )
        | Register, Some c -> Record(toEvent r Upgraded (Some c.Version) Present)
        // ---------------------------------------------------------- verify
        | Verify, Some c when c.State = Present && sameVersion c -> Record(toEvent r Verified None Present)
        | Verify, _ ->
            Refused(
                "verification-mismatch",
                $"{SystemId.value r.SystemId} {r.SystemVersion.Text} is not the recorded current installation on {Target.key r.Target}; use installation.reconcile to record observed state"
            )
        // ---------------------------------------------------------- remove
        | Remove, Some c when c.State = Absent -> Unchanged c
        | Remove, None ->
            Refused("not-installed", $"no installation of {SystemId.value r.SystemId} is recorded on {Target.key r.Target}")
        | Remove, Some c when not (sameVersion c) && c.State = Present ->
            Refused(
                "version-mismatch",
                $"recorded {SystemId.value r.SystemId} on {Target.key r.Target} is {c.Version.Text}, not {r.SystemVersion.Text}"
            )
        | Remove, Some c -> Record(toEvent r Removed (Some c.Version) Absent)
        // ------------------------------------------------------- reconcile
        | Reconcile observed, Some c when c.State = observed && sameVersion c -> Unchanged c
        | Reconcile observed, current ->
            Record(toEvent r Reconciled (current |> Option.map (fun c -> c.Version)) observed)
