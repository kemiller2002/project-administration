/// Rules a request must satisfy before anything is written.
///
/// The privacy guard exists because an installation record leaves the
/// machine it describes. A value that looks like a local absolute path, a
/// home directory, an email address or a MAC address is refused rather than
/// stored; the caller must supply an explicit logical value instead.
module Administration.Installations.Validation

open System
open Administration.Installations.Model

type ValidationError =
    | ModelRefused of ModelError
    | MissingOperationId
    | OperationIdTooLong
    | PrivacyRefused of field: string * reason: string
    | UnknownSystem of SystemId
    | ValueTooLong of field: string

let private isHex (c: char) = Char.IsAsciiHexDigit c

let private looksLikeMacAddress (value: string) =
    // Six hex pairs separated by ':' or '-' anywhere in the value.
    let chars = value.ToCharArray()

    seq { 0 .. chars.Length - 17 }
    |> Seq.exists (fun i ->
        let window = String(chars, i, 17)
        let sep = window[2]

        (sep = ':' || sep = '-')
        && [ 0..5 ] |> List.forall (fun k -> isHex window[k * 3] && isHex window[k * 3 + 1])
        && [ 0..4 ] |> List.forall (fun k -> window[k * 3 + 2] = sep))

let private looksLikeEmail (value: string) =
    value.Split([| ' '; ','; ';'; '<'; '>' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.exists (fun token ->
        match token.Split('@') with
        | [| local; domain |] when local.Length > 0 && not (local.Contains '/') ->
            let labels = domain.Split('.')

            labels.Length >= 2
            && labels |> Array.forall (fun l -> l.Length > 0)
            && labels[labels.Length - 1] |> Seq.forall Char.IsAsciiLetter
            && labels[labels.Length - 1].Length >= 2
        | _ -> false)

let private looksLikeLocalPath (value: string) =
    let v = value.Trim()

    v.StartsWith("/", StringComparison.Ordinal)
    || v.StartsWith("~", StringComparison.Ordinal)
    || v.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
    || v.StartsWith("\\\\", StringComparison.Ordinal)
    || (v.Length >= 3 && Char.IsAsciiLetter v[0] && v[1] = ':' && (v[2] = '\\' || v[2] = '/'))
    || v.Contains("/home/", StringComparison.Ordinal)
    || v.Contains("/Users/", StringComparison.Ordinal)
    || v.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase)

/// Refuse a free-text value that would leak identifying machine data.
let guard (field: string) (value: string) : Result<unit, ValidationError> =
    if value.Length > 512 then Error(ValueTooLong field)
    elif looksLikeLocalPath value then Error(PrivacyRefused(field, "looks like a local filesystem path"))
    elif looksLikeEmail value then Error(PrivacyRefused(field, "looks like an email address"))
    elif looksLikeMacAddress value then Error(PrivacyRefused(field, "looks like a MAC address"))
    else Ok()

let private guardOptional field value =
    match value with
    | Some v -> guard field v
    | None -> Ok()

let private firstError (checks: Result<unit, ValidationError> list) =
    checks |> List.tryPick (fun r -> match r with Error e -> Some e | Ok() -> None)

/// Validate a request as a whole. `knownSystems` is the Echelon Registry
/// catalog when one is available; `None` means the catalog is unavailable,
/// which must not stop an installation from being recorded.
let request (knownSystems: Set<string> option) (r: InstallationRequest) : Result<InstallationRequest, ValidationError> =
    let checks =
        [ (if String.IsNullOrWhiteSpace r.OperationId then Error MissingOperationId else Ok())
          (if r.OperationId.Length > 200 then Error OperationIdTooLong else Ok())
          guard "operationId" r.OperationId
          guardOptional "correlationId" r.CorrelationId
          guardOptional "source.repository" r.Source.Repository
          guardOptional "source.distribution" r.Source.Distribution
          guardOptional "source.release" r.Source.Release
          guardOptional "source.artifact" r.Source.Artifact
          guardOptional "source.digest" r.Source.Digest
          yield! r.Evidence |> List.map (fun e -> guard "evidence.reference" e.Reference)
          yield! r.Evidence |> List.map (fun e -> guard "evidence.kind" e.Kind)
          (match r.Actor with
           | Some a -> guard "actor.id" a.Id
           | None -> Ok())
          (match r.Execution with
           | Some e ->
               match guard "execution.id" e.ExecutionId with
               | Ok() -> guardOptional "execution.workItem" e.WorkItem
               | error -> error
           | None -> Ok())
          (match knownSystems with
           | Some known when not (known.Contains(SystemId.value r.SystemId)) -> Error(UnknownSystem r.SystemId)
           | _ -> Ok()) ]

    match firstError checks with
    | Some error -> Error error
    | None -> Ok r

let describe (error: ValidationError) =
    match error with
    | ModelRefused(InvalidSystemId v) -> $"invalid system id '{v}' (expected ^[a-z][a-z0-9-]*$)"
    | ModelRefused(InvalidVersion v) -> $"invalid version '{v}' (expected SemVer 2.0)"
    | ModelRefused(InvalidRepositoryTarget v) -> $"invalid repository target '{v}' (expected owner/repository)"
    | ModelRefused(InvalidEnvironmentTarget v) ->
        $"invalid environment target '{v}' (expected an explicit logical id: lowercase letters, digits, '.', '_', '-')"
    | MissingOperationId -> "operationId is required (it is the idempotency key)"
    | OperationIdTooLong -> "operationId is longer than 200 characters"
    | PrivacyRefused(field, reason) -> $"{field} refused: {reason}; supply an explicit logical value instead"
    | UnknownSystem id -> $"system '{SystemId.value id}' is not in the Echelon Registry catalog"
    | ValueTooLong field -> $"{field} is longer than 512 characters"

let code (error: ValidationError) =
    match error with
    | ModelRefused _ -> "invalid-identity"
    | MissingOperationId
    | OperationIdTooLong -> "invalid-operation-id"
    | PrivacyRefused _ -> "privacy-refused"
    | UnknownSystem _ -> "unknown-system"
    | ValueTooLong _ -> "value-too-long"
