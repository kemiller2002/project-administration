/// The installation inventory's vocabulary.
///
/// Project Administration records *what Echelon system is installed where*.
/// It does not catalog which systems exist (Echelon Registry does) and it is
/// never the store for repository-local work execution (Praxis is). History
/// is canonical: every installation fact is an immutable event; the current
/// state is a projection computed from that history.
module Administration.Installations.Model

open System

/// A catalog system identifier, e.g. `praxis`, `ordo`, `conditor`.
type SystemId = private SystemId of string with
    member this.Value = let (SystemId v) = this in v
    override this.ToString() = this.Value

/// A semantic version (SemVer 2.0). Build metadata is preserved but ignored
/// for precedence, as SemVer requires.
type SemVer =
    private
        { major: int
          minor: int
          patch: int
          prerelease: string list
          build: string option
          text: string }

    member this.Major = this.major
    member this.Minor = this.minor
    member this.Patch = this.patch
    member this.Prerelease = this.prerelease
    member this.Text = this.text
    override this.ToString() = this.text

/// Where a system is installed.
///
/// A repository target uses the repository's stable identity (`owner/repo`).
/// An environment target — a workstation, a CI image, a server — uses an
/// explicitly configured logical ID. Nothing here can be derived from a
/// hostname, username, home directory, MAC address or hardware serial; the
/// validation rules refuse values shaped like them.
type TargetKind =
    | Repository
    | Environment

type Target =
    private
        { kind: TargetKind
          id: string }

    member this.Kind = this.kind
    member this.Id = this.id

/// Which release/artifact produced the installation. Every field is optional
/// because not every installer knows every fact; an unknown value is left
/// unset, never guessed.
type InstallationSource =
    { Repository: string option
      Distribution: string option
      Release: string option
      Artifact: string option
      Digest: string option }

/// A reference to evidence that proves the installation: a receipt, a lock
/// file entry, a workflow run, a release asset.
type EvidenceReference =
    { Kind: string
      Reference: string
      Digest: string option }

type ActorKind =
    | Human
    | Agent
    | Automation

/// Who performed the installation, when known. Self-reported by the caller.
type InstallationActor =
    { Kind: ActorKind
      Id: string
      Provider: string option
      Model: string option
      Runtime: string option }

/// The execution that performed the installation, when known.
type ExecutionReference =
    { ExecutionId: string
      WorkItem: string option
      Repository: string option }

/// What an event records.
type InstallationOperation =
    | Installed
    | Upgraded
    | Verified
    | Removed
    /// An explicit observation of actual state, used after drift or an
    /// unknown installer outcome.
    | Reconciled

/// The state an installation is in.
type InstallationState =
    | Present
    | Absent
    /// Reconciliation could not determine whether it is installed.
    | Indeterminate

/// One immutable, canonical history event.
type InstallationEvent =
    { SchemaVersion: int
      EventId: string
      OperationId: string
      CorrelationId: string option
      OccurredAt: DateTimeOffset
      Operation: InstallationOperation
      SystemId: SystemId
      SystemVersion: SemVer
      PreviousVersion: SemVer option
      Target: Target
      Source: InstallationSource
      Evidence: EvidenceReference list
      Actor: InstallationActor option
      Execution: ExecutionReference option
      /// The resulting state. Explicit on every event so a reader never has
      /// to re-derive it from the operation.
      ResultingState: InstallationState }

/// The capability a request exercises.
type Capability =
    | Register
    | Verify
    | Remove
    | Reconcile of observed: InstallationState

/// A typed request from a spoke system. The request contract is the
/// integration boundary; spokes never see the event layout.
type InstallationRequest =
    { OperationId: string
      CorrelationId: string option
      OccurredAt: DateTimeOffset
      Capability: Capability
      SystemId: SystemId
      SystemVersion: SemVer
      Target: Target
      Source: InstallationSource
      Evidence: EvidenceReference list
      Actor: InstallationActor option
      Execution: ExecutionReference option }

/// The current-state projection of one (target, system) pair.
type CurrentInstallation =
    { Target: Target
      SystemId: SystemId
      State: InstallationState
      Version: SemVer
      /// Every distinct earlier version, oldest first.
      PreviousVersions: SemVer list
      FirstInstalledAt: DateTimeOffset
      LastChangedAt: DateTimeOffset
      LastObservedAt: DateTimeOffset
      Source: InstallationSource
      Execution: ExecutionReference option
      LastEventId: string
      EventCount: int }

[<Literal>]
let EventSchema = "echelon.installation-event/v1"

[<Literal>]
let EventSchemaVersion = 1

[<Literal>]
let RequestSchema = "echelon.installation.request/v1"

[<Literal>]
let ResultSchema = "echelon.installation.result/v1"

[<Literal>]
let ProjectionSchema = "echelon.installations-current/v1"

/// The version of this receiving system's integration contract.
[<Literal>]
let IntegrationVersion = "1.0.0"

/// Why a value could not become a checked identity.
type ModelError =
    | InvalidSystemId of string
    | InvalidVersion of string
    | InvalidRepositoryTarget of string
    | InvalidEnvironmentTarget of string

let private isLowerAlnum (c: char) = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
let private isAlnum (c: char) = isLowerAlnum c || (c >= 'A' && c <= 'Z')

[<RequireQualifiedAccess>]
module SystemId =

    /// Same shape as the Echelon Registry's system id: `^[a-z][a-z0-9-]*$`.
    let create (raw: string) =
        if
            not (String.IsNullOrEmpty raw)
            && raw.Length <= 64
            && Char.IsAsciiLetterLower raw[0]
            && raw |> Seq.forall (fun c -> isLowerAlnum c || c = '-')
        then
            Ok(SystemId raw)
        else
            Error(InvalidSystemId raw)

    let value (SystemId v) = v

[<RequireQualifiedAccess>]
module SemVer =

    let private numeric (s: string) =
        if s.Length > 0 && s |> Seq.forall Char.IsAsciiDigit && (s = "0" || s[0] <> '0') then
            match Int32.TryParse s with
            | true, n -> Some n
            | _ -> None
        else
            None

    let private identifiers (s: string) =
        let parts = s.Split('.')

        if parts |> Array.forall (fun p -> p.Length > 0 && p |> Seq.forall (fun c -> isAlnum c || c = '-')) then
            Some(List.ofArray parts)
        else
            None

    let parse (raw: string) =
        if String.IsNullOrWhiteSpace raw || raw.Trim() <> raw then
            Error(InvalidVersion raw)
        else
            let core, build =
                match raw.IndexOf '+' with
                | -1 -> raw, None
                | i -> raw.Substring(0, i), Some(raw.Substring(i + 1))

            let release, pre =
                match core.IndexOf '-' with
                | -1 -> core, None
                | i -> core.Substring(0, i), Some(core.Substring(i + 1))

            let preIds = pre |> Option.map identifiers
            let buildIds = build |> Option.map identifiers

            match release.Split('.') |> Array.map numeric, preIds, buildIds with
            | [| Some major; Some minor; Some patch |], (None | Some(Some _)), (None | Some(Some _)) ->
                Ok
                    { major = major
                      minor = minor
                      patch = patch
                      prerelease = preIds |> Option.flatten |> Option.defaultValue []
                      build = build
                      text = raw }
            | _ -> Error(InvalidVersion raw)

    let private compareIdentifier (a: string) (b: string) =
        match numeric a, numeric b with
        | Some x, Some y -> compare x y
        | Some _, None -> -1
        | None, Some _ -> 1
        | None, None -> String.CompareOrdinal(a, b)

    /// SemVer precedence. Build metadata does not participate.
    let compareVersions (a: SemVer) (b: SemVer) =
        let core = compare (a.major, a.minor, a.patch) (b.major, b.minor, b.patch)

        if core <> 0 then
            core
        else
            match a.prerelease, b.prerelease with
            | [], [] -> 0
            | [], _ -> 1
            | _, [] -> -1
            | xs, ys ->
                let rec go xs ys =
                    match xs, ys with
                    | [], [] -> 0
                    | [], _ -> -1
                    | _, [] -> 1
                    | x :: xt, y :: yt ->
                        match compareIdentifier x y with
                        | 0 -> go xt yt
                        | c -> c

                go xs ys

    let text (v: SemVer) = v.text

[<RequireQualifiedAccess>]
module Target =

    /// A repository's stable identity: `owner/repository`.
    let repository (raw: string) =
        match raw.Split('/') with
        | [| owner; name |] when
            owner.Length > 0
            && name.Length > 0
            && owner |> Seq.forall (fun c -> isAlnum c || c = '-')
            && name |> Seq.forall (fun c -> isAlnum c || c = '-' || c = '_' || c = '.')
            && name <> "."
            && name <> ".."
            ->
            Ok { kind = Repository; id = raw }
        | _ -> Error(InvalidRepositoryTarget raw)

    /// An explicitly configured logical environment ID: lowercase letters,
    /// digits, `.`, `_` and `-`, starting with a letter or digit, at most 64
    /// characters. No `/`, `\`, `:` or `@`, so a path, a URL, an email or a
    /// MAC address cannot be stored as one.
    let environment (raw: string) =
        if
            not (String.IsNullOrEmpty raw)
            && raw.Length <= 64
            && isLowerAlnum raw[0]
            && raw |> Seq.forall (fun c -> isLowerAlnum c || c = '.' || c = '_' || c = '-')
        then
            Ok { kind = Environment; id = raw }
        else
            Error(InvalidEnvironmentTarget raw)

    let create kind raw =
        match kind with
        | Repository -> repository raw
        | Environment -> environment raw

    let kindToWire kind =
        match kind with
        | Repository -> "repository"
        | Environment -> "environment"

    let kindFromWire raw =
        match raw with
        | "repository" -> Some Repository
        | "environment" -> Some Environment
        | _ -> None

    /// `repository:owner/name` or `environment:id`.
    let key (t: Target) = kindToWire t.kind + ":" + t.id

    let parseKey (raw: string) =
        match raw.IndexOf ':' with
        | -1 -> Error(InvalidRepositoryTarget raw)
        | i ->
            match kindFromWire (raw.Substring(0, i)) with
            | Some kind -> create kind (raw.Substring(i + 1))
            | None -> Error(InvalidRepositoryTarget raw)

[<RequireQualifiedAccess>]
module InstallationOperation =

    let toWire op =
        match op with
        | Installed -> "installed"
        | Upgraded -> "upgraded"
        | Verified -> "verified"
        | Removed -> "removed"
        | Reconciled -> "reconciled"

    let fromWire raw =
        [ Installed; Upgraded; Verified; Removed; Reconciled ] |> List.tryFind (fun o -> toWire o = raw)

[<RequireQualifiedAccess>]
module InstallationState =

    let toWire state =
        match state with
        | Present -> "installed"
        | Absent -> "removed"
        | Indeterminate -> "indeterminate"

    let fromWire raw =
        match raw with
        | "installed" -> Some Present
        | "removed" -> Some Absent
        | "indeterminate"
        | "unknown" -> Some Indeterminate
        | _ -> None

[<RequireQualifiedAccess>]
module ActorKind =

    let toWire kind =
        match kind with
        | Human -> "human"
        | Agent -> "agent"
        | Automation -> "automation"

    let fromWire raw =
        match raw with
        | "human" -> Some Human
        | "agent" -> Some Agent
        | "automation" -> Some Automation
        | _ -> None

[<RequireQualifiedAccess>]
module Capability =

    let toWire capability =
        match capability with
        | Register -> "installation.register"
        | Verify -> "installation.verify"
        | Remove -> "installation.remove"
        | Reconcile _ -> "installation.reconcile"
