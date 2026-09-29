/// JSON shapes of the installation inventory. Field names are written here,
/// never derived from F# names, so renaming a type cannot change a stored
/// event or the request contract.
module Administration.Installations.Wire

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes
open Administration.Installations.Model

type WireError = WireError of path: string * message: string

let private str (value: string) : JsonNode = JsonValue.Create value
let private optStr (value: string option) : JsonNode = value |> Option.map str |> Option.toObj

let private obj (fields: (string * JsonNode) list) : JsonNode =
    let o = JsonObject()
    fields |> List.iter (fun (k, v) -> o[k] <- v)
    o

let private arr (items: JsonNode list) : JsonNode =
    let a = JsonArray()
    items |> List.iter a.Add
    a

let timestamp (at: DateTimeOffset) =
    at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)

let parseTimestamp (raw: string) =
    match DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) with
    | true, v when raw.Contains 'T' -> Ok(v.ToUniversalTime())
    | _ -> Error(WireError("occurredAt", $"'{raw}' is not an RFC 3339 timestamp"))

let private sourceToJson (s: InstallationSource) =
    obj
        [ "repository", optStr s.Repository
          "distribution", optStr s.Distribution
          "release", optStr s.Release
          "artifact", optStr s.Artifact
          "digest", optStr s.Digest ]

let private targetToJson (t: Target) =
    obj [ "kind", str (Target.kindToWire t.Kind); "id", str t.Id ]

let private evidenceToJson (e: EvidenceReference) =
    obj [ "kind", str e.Kind; "reference", str e.Reference; "digest", optStr e.Digest ]

let private actorToJson (a: InstallationActor option) =
    match a with
    | None -> null
    | Some a ->
        obj
            [ "kind", str (ActorKind.toWire a.Kind)
              "id", str a.Id
              "provider", optStr a.Provider
              "model", optStr a.Model
              "runtime", optStr a.Runtime ]

let private executionToJson (e: ExecutionReference option) =
    match e with
    | None -> null
    | Some e -> obj [ "id", str e.ExecutionId; "workItem", optStr e.WorkItem; "repository", optStr e.Repository ]

let eventToJson (e: InstallationEvent) : JsonNode =
    obj
        [ "schema", str EventSchema
          "schemaVersion", JsonValue.Create e.SchemaVersion
          "eventId", str e.EventId
          "operationId", str e.OperationId
          "correlationId", optStr e.CorrelationId
          "occurredAt", str (timestamp e.OccurredAt)
          "operation", str (InstallationOperation.toWire e.Operation)
          "systemId", str (SystemId.value e.SystemId)
          "systemVersion", str (SemVer.text e.SystemVersion)
          "previousVersion", optStr (e.PreviousVersion |> Option.map SemVer.text)
          "target", targetToJson e.Target
          "state", str (InstallationState.toWire e.ResultingState)
          "source", sourceToJson e.Source
          "installationEvidence", arr (e.Evidence |> List.map evidenceToJson)
          "actor", actorToJson e.Actor
          "execution", executionToJson e.Execution ]

let private options =
    JsonSerializerOptions(WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

let render (node: JsonNode) = node.ToJsonString options + "\n"

// ------------------------------------------------------------------ reading

let private member' (path: string) (name: string) (node: JsonNode) : Result<JsonNode option, WireError> =
    match node with
    | :? JsonObject as o ->
        let mutable found: JsonNode = null

        if o.TryGetPropertyValue(name, &found) then
            Ok(Option.ofObj found)
        else
            Ok None
    | _ -> Error(WireError(path, "expected an object"))

let private requiredString path name node =
    member' path name node
    |> Result.bind (fun v ->
        match v with
        | Some(:? JsonValue as jv) ->
            match jv.TryGetValue<string>() with
            | true, s -> Ok s
            | _ -> Error(WireError(path + "." + name, "expected a string"))
        | _ -> Error(WireError(path + "." + name, "is required")))

let private optionalString path name node =
    member' path name node
    |> Result.bind (fun v ->
        match v with
        | None -> Ok None
        | Some(:? JsonValue as jv) ->
            match jv.TryGetValue<string>() with
            | true, s -> Ok(Some s)
            | _ -> Error(WireError(path + "." + name, "expected a string or null"))
        | Some _ -> Error(WireError(path + "." + name, "expected a string or null")))

let private optionalObject path name node =
    member' path name node
    |> Result.bind (fun v ->
        match v with
        | None -> Ok None
        | Some(:? JsonObject as o) -> Ok(Some(o :> JsonNode))
        | Some _ -> Error(WireError(path + "." + name, "expected an object or null")))

let private array path name node =
    member' path name node
    |> Result.bind (fun v ->
        match v with
        | None -> Ok []
        | Some(:? JsonArray as a) -> Ok(a |> Seq.map (fun x -> x) |> Seq.toList)
        | Some _ -> Error(WireError(path + "." + name, "expected an array")))

let private traverse (f: 'a -> Result<'b, 'e>) (xs: 'a list) : Result<'b list, 'e> =
    List.foldBack (fun x acc -> Result.bind (fun ys -> f x |> Result.map (fun y -> y :: ys)) acc) xs (Ok [])

let private modelError path (r: Result<'a, ModelError>) =
    r |> Result.mapError (fun e -> WireError(path, sprintf "%A" e))

let private target path node =
    requiredString path "kind" node
    |> Result.bind (fun kind ->
        match Target.kindFromWire kind with
        | None -> Error(WireError(path + ".kind", "expected repository or environment"))
        | Some k -> requiredString path "id" node |> Result.bind (Target.create k >> modelError (path + ".id")))

let private source path (node: JsonNode option) =
    match node with
    | None ->
        Ok
            { Repository = None
              Distribution = None
              Release = None
              Artifact = None
              Digest = None }
    | Some n ->
        optionalString path "repository" n
        |> Result.bind (fun repository ->
            optionalString path "distribution" n
            |> Result.bind (fun distribution ->
                optionalString path "release" n
                |> Result.bind (fun release ->
                    optionalString path "artifact" n
                    |> Result.bind (fun artifact ->
                        optionalString path "digest" n
                        |> Result.map (fun digest ->
                            { Repository = repository
                              Distribution = distribution
                              Release = release
                              Artifact = artifact
                              Digest = digest })))))

let private evidence path node =
    requiredString path "kind" node
    |> Result.bind (fun kind ->
        requiredString path "reference" node
        |> Result.bind (fun reference ->
            optionalString path "digest" node
            |> Result.map (fun digest -> { Kind = kind; Reference = reference; Digest = digest })))

let private actor path (node: JsonNode option) =
    match node with
    | None -> Ok None
    | Some n ->
        requiredString path "kind" n
        |> Result.bind (fun kind ->
            match ActorKind.fromWire kind with
            | None -> Error(WireError(path + ".kind", "expected human, agent or automation"))
            | Some k ->
                requiredString path "id" n
                |> Result.bind (fun id ->
                    optionalString path "provider" n
                    |> Result.bind (fun provider ->
                        optionalString path "model" n
                        |> Result.bind (fun model ->
                            optionalString path "runtime" n
                            |> Result.map (fun runtime ->
                                Some
                                    { Kind = k
                                      Id = id
                                      Provider = provider
                                      Model = model
                                      Runtime = runtime })))))

let private execution path (node: JsonNode option) =
    match node with
    | None -> Ok None
    | Some n ->
        requiredString path "id" n
        |> Result.bind (fun id ->
            optionalString path "workItem" n
            |> Result.bind (fun workItem ->
                optionalString path "repository" n
                |> Result.map (fun repository ->
                    Some
                        { ExecutionId = id
                          WorkItem = workItem
                          Repository = repository })))

let private version path raw =
    SemVer.parse raw |> modelError path

let private systemId path raw = SystemId.create raw |> modelError path

let private childObject path name node =
    optionalObject path name node

type private ResultBuilder() =
    member _.Bind(r: Result<'a, 'e>, f: 'a -> Result<'b, 'e>) = Result.bind f r
    member _.Return(v: 'a) : Result<'a, 'e> = Ok v
    member _.ReturnFrom(r: Result<'a, 'e>) = r

let private result = ResultBuilder()

let private requiredTarget path node =
    childObject path "target" node
    |> Result.bind (fun t ->
        match t with
        | None -> Error(WireError(path + ".target", "is required"))
        | Some t -> target (path + ".target") t)

/// Read a request document (`echelon.installation.request/v1`).
let requestFromJson (node: JsonNode) : Result<InstallationRequest, WireError> =
    let p = "$"

    result {
        let! schema = requiredString p "schema" node

        do!
            if schema <> RequestSchema then
                Error(WireError("$.schema", $"expected {RequestSchema}"))
            else
                Ok()

        let! capabilityName = requiredString p "capability" node

        let! capability =
            match capabilityName with
            | "installation.register" -> Ok Register
            | "installation.verify" -> Ok Verify
            | "installation.remove" -> Ok Remove
            | "installation.reconcile" ->
                requiredString p "observedState" node
                |> Result.bind (fun s ->
                    match InstallationState.fromWire s with
                    | Some state -> Ok(Reconcile state)
                    | None -> Error(WireError("$.observedState", "expected installed, removed or indeterminate")))
            | other -> Error(WireError("$.capability", $"unsupported capability '{other}'"))

        let! operationId = requiredString p "operationId" node
        let! correlationId = optionalString p "correlationId" node
        let! occurredAt = requiredString p "occurredAt" node |> Result.bind parseTimestamp
        let! sid = requiredString p "systemId" node |> Result.bind (systemId "$.systemId")
        let! ver = requiredString p "systemVersion" node |> Result.bind (version "$.systemVersion")
        let! tgt = requiredTarget p node
        let! src = childObject p "source" node |> Result.bind (source "$.source")
        let! ev = array p "evidence" node |> Result.bind (traverse (evidence "$.evidence[]"))
        let! act = childObject p "actor" node |> Result.bind (actor "$.actor")
        let! exe = childObject p "execution" node |> Result.bind (execution "$.execution")

        return
            { OperationId = operationId
              CorrelationId = correlationId
              OccurredAt = occurredAt
              Capability = capability
              SystemId = sid
              SystemVersion = ver
              Target = tgt
              Source = src
              Evidence = ev
              Actor = act
              Execution = exe }
    }

/// Read a stored event.
let eventFromJson (node: JsonNode) : Result<InstallationEvent, WireError> =
    let p = "$"

    result {
        let! schema = requiredString p "schema" node

        do!
            if schema <> EventSchema then
                Error(WireError("$.schema", $"expected {EventSchema}"))
            else
                Ok()

        let! eventId = requiredString p "eventId" node
        let! operationId = requiredString p "operationId" node
        let! correlationId = optionalString p "correlationId" node
        let! occurredAt = requiredString p "occurredAt" node |> Result.bind parseTimestamp
        let! opName = requiredString p "operation" node

        let! operation =
            match InstallationOperation.fromWire opName with
            | Some o -> Ok o
            | None -> Error(WireError("$.operation", $"unknown operation '{opName}'"))

        let! sid = requiredString p "systemId" node |> Result.bind (systemId "$.systemId")
        let! ver = requiredString p "systemVersion" node |> Result.bind (version "$.systemVersion")
        let! prevRaw = optionalString p "previousVersion" node

        let! previous =
            match prevRaw with
            | None -> Ok None
            | Some v -> version "$.previousVersion" v |> Result.map Some

        let! tgt = requiredTarget p node
        let! stateName = requiredString p "state" node

        let! state =
            match InstallationState.fromWire stateName with
            | Some st -> Ok st
            | None -> Error(WireError("$.state", $"unknown state '{stateName}'"))

        let! src = childObject p "source" node |> Result.bind (source "$.source")
        let! ev = array p "installationEvidence" node |> Result.bind (traverse (evidence "$.installationEvidence[]"))
        let! act = childObject p "actor" node |> Result.bind (actor "$.actor")
        let! exe = childObject p "execution" node |> Result.bind (execution "$.execution")

        return
            { SchemaVersion = EventSchemaVersion
              EventId = eventId
              OperationId = operationId
              CorrelationId = correlationId
              OccurredAt = occurredAt
              Operation = operation
              SystemId = sid
              SystemVersion = ver
              PreviousVersion = previous
              Target = tgt
              Source = src
              Evidence = ev
              Actor = act
              Execution = exe
              ResultingState = state }
    }

let currentToJson (c: CurrentInstallation) : JsonNode =
    obj
        [ "target", targetToJson c.Target
          "systemId", str (SystemId.value c.SystemId)
          "state", str (InstallationState.toWire c.State)
          "version", str (SemVer.text c.Version)
          "previousVersions", arr (c.PreviousVersions |> List.map (SemVer.text >> str))
          "firstInstalledAt", str (timestamp c.FirstInstalledAt)
          "lastChangedAt", str (timestamp c.LastChangedAt)
          "lastObservedAt", str (timestamp c.LastObservedAt)
          "source", sourceToJson c.Source
          "execution", executionToJson c.Execution
          "lastEventId", str c.LastEventId
          "eventCount", JsonValue.Create c.EventCount ]

let projectionToJson (current: CurrentInstallation list) (eventCount: int) (lastEventId: string option) : JsonNode =
    obj
        [ "schema", str ProjectionSchema
          "canonical", JsonValue.Create false
          "note", str "Generated from installations/events. History is canonical; regenerate with `administration installation project`."
          "eventCount", JsonValue.Create eventCount
          "lastEventId", optStr lastEventId
          "installations", arr (current |> List.map currentToJson) ]

let parse (text: string) : Result<JsonNode, WireError> =
    try
        match JsonNode.Parse text with
        | null -> Error(WireError("$", "empty document"))
        | node -> Ok node
    with :? JsonException as ex ->
        Error(WireError("$", ex.Message))

let describe (WireError(path, message)) = $"{path}: {message}"
