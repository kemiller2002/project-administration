/// Questions the inventory answers. Query semantics live here, not in any
/// UI: a CLI, an API or a future Forma page all call these functions.
module Administration.Installations.Queries

open System
open Administration.Installations.Model

/// A filter over the current-state projection. Every criterion is optional;
/// an empty filter returns every installation.
type Filter =
    { Target: Target option
      System: SystemId option
      Version: SemVer option
      OlderThan: SemVer option
      State: InstallationState option }

let everything =
    { Target = None
      System = None
      Version = None
      OlderThan = None
      State = None }

let private admits (f: Filter) (c: CurrentInstallation) =
    let matches opt test = opt |> Option.forall test

    matches f.Target (fun t -> c.Target = t)
    && matches f.System (fun s -> c.SystemId = s)
    && matches f.Version (fun v -> c.Version.Text = v.Text)
    && matches f.OlderThan (fun v -> c.State = Present && SemVer.compareVersions c.Version v < 0)
    && matches f.State (fun s -> c.State = s)

let select (f: Filter) (current: CurrentInstallation list) = current |> List.filter (admits f)

/// Installations whose last observation is older than the given age. Staleness
/// depends on "now", so it is computed at query time and never stored in the
/// deterministic projection.
let stale (now: DateTimeOffset) (maxAge: TimeSpan) (current: CurrentInstallation list) =
    current |> List.filter (fun c -> c.State = Present && now - c.LastObservedAt > maxAge)

/// The ordered history of a target, optionally narrowed to one system. Removal
/// never deletes history, so this includes removed installations.
let history (target: Target) (system: SystemId option) (events: InstallationEvent list) =
    events
    |> List.filter (fun e -> e.Target = target && system |> Option.forall (fun s -> e.SystemId = s))
    |> Inventory.order

/// The presentation status of a current installation, including `stale`
/// when a maximum age is supplied.
let status (now: DateTimeOffset) (maxAge: TimeSpan option) (c: CurrentInstallation) =
    match c.State, maxAge with
    | Present, Some age when now - c.LastObservedAt > age -> "stale"
    | state, _ -> InstallationState.toWire state
