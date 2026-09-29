/// `administration` — Project Administration's receiving-system executable.
///
/// Spoke systems (Praxis, Conditor, installers) call this executable through
/// the typed `installation.*` capability contract; they never read or write
/// the inventory's files. The same executable runs locally against a
/// checkout and inside this repository's GitHub workflow transport.
module Administration.Cli.Program

open System
open System.IO
open System.Text.Json.Nodes
open Aegis
open Administration.Installations
open Administration.Installations.Model

/// Documented exit codes.
[<RequireQualifiedAccess>]
module Exit =
    let Success = 0
    let InvalidArguments = 2
    let Refused = 3
    let StoreFailure = 4

let usage =
    """administration — Project Administration installation inventory

  administration version [--json]
  administration installation register  --request FILE|- | <fields>   [--store DIR] [--catalog FILE] [--json]
  administration installation verify    --request FILE|- | <fields>   [--store DIR] [--json]
  administration installation remove    --request FILE|- | <fields>   [--store DIR] [--json]
  administration installation reconcile --request FILE|- | <fields> --observed-state installed|removed|indeterminate
  administration installation query   [--target KIND:ID] [--system ID] [--version V] [--older-than V]
                                      [--state installed|removed|indeterminate] [--stale-after-days N --now T]
                                      [--store DIR] [--json]
  administration installation history --target KIND:ID [--system ID] [--store DIR] [--json]
  administration installation project [--check] [--store DIR]

<fields>: --operation-id ID --occurred-at RFC3339 --system ID --version SEMVER
          --target-kind repository|environment --target-id ID
          [--source-repository OWNER/REPO] [--distribution CHANNEL] [--release TAG]
          [--artifact ID] [--digest DIGEST] [--evidence KIND=REFERENCE]...
          [--actor-kind human|agent|automation --actor-id ID [--provider P] [--model M] [--runtime R]]
          [--execution ID [--work-item ID] [--execution-repository OWNER/REPO]]

Exit codes: 0 success, 2 invalid arguments or request, 3 refused, 4 store failure."""

let private tryOption (name: string) (args: string list) =
    let rec go xs =
        match xs with
        | flag :: value :: _ when flag = name -> Some value
        | _ :: rest -> go rest
        | [] -> None

    go args

let private options (name: string) (args: string list) =
    let rec go acc xs =
        match xs with
        | flag :: value :: rest when flag = name -> go (value :: acc) rest
        | _ :: rest -> go acc rest
        | [] -> List.rev acc

    go [] args

let private hasFlag name (args: string list) = List.contains name args

let private str (v: string) : JsonNode = JsonValue.Create v
let private optStr (v: string option) : JsonNode = v |> Option.map str |> Option.toObj

let private obj (fields: (string * JsonNode) list) : JsonNode =
    let o = JsonObject()
    fields |> List.iter (fun (k, v) -> o[k] <- v)
    o

/// Build a request document from command-line fields, so operators and
/// callers without a JSON file use exactly the same typed contract.
let private requestFromFlags (capability: string) (args: string list) : JsonNode =
    let opt name = tryOption name args

    let evidence =
        options "--evidence" args
        |> List.map (fun e ->
            match e.IndexOf '=' with
            | -1 -> obj [ "kind", str "reference"; "reference", str e; "digest", null ]
            | i -> obj [ "kind", str (e.Substring(0, i)); "reference", str (e.Substring(i + 1)); "digest", null ])

    let evidenceArray = JsonArray()
    evidence |> List.iter evidenceArray.Add

    let actor =
        match opt "--actor-kind", opt "--actor-id" with
        | Some kind, Some id ->
            obj
                [ "kind", str kind
                  "id", str id
                  "provider", optStr (opt "--provider")
                  "model", optStr (opt "--model")
                  "runtime", optStr (opt "--runtime") ]
        | _ -> null

    let execution =
        match opt "--execution" with
        | Some id ->
            obj
                [ "id", str id
                  "workItem", optStr (opt "--work-item")
                  "repository", optStr (opt "--execution-repository") ]
        | None -> null

    obj
        [ "schema", str RequestSchema
          "capability", str capability
          "operationId", optStr (opt "--operation-id")
          "correlationId", optStr (opt "--correlation-id")
          "occurredAt", optStr (opt "--occurred-at")
          "systemId", optStr (opt "--system")
          "systemVersion", optStr (opt "--version")
          "observedState", optStr (opt "--observed-state")
          "target", obj [ "kind", optStr (opt "--target-kind"); "id", optStr (opt "--target-id") ]
          "source",
          obj
              [ "repository", optStr (opt "--source-repository")
                "distribution", optStr (opt "--distribution")
                "release", optStr (opt "--release")
                "artifact", optStr (opt "--artifact")
                "digest", optStr (opt "--digest") ]
          "evidence", evidenceArray
          "actor", actor
          "execution", execution ]

let private readRequest (stdin: TextReader) (capability: string) (args: string list) =
    match tryOption "--request" args with
    | Some "-" -> Wire.parse (stdin.ReadToEnd())
    | Some file when File.Exists file -> Wire.parse (File.ReadAllText file)
    | Some file -> Error(Wire.WireError("--request", $"file not found: {file}"))
    | None -> Ok(requestFromFlags capability args)

/// The catalog of known systems, read from an Echelon Registry
/// `registry/systems.json`. Optional: its absence never blocks recording.
let private readCatalog (args: string list) =
    match tryOption "--catalog" args with
    | None -> Ok None
    | Some file when not (File.Exists file) -> Ok None
    | Some file ->
        Wire.parse (File.ReadAllText file)
        |> Result.map (fun node ->
            match node with
            | :? JsonObject as o ->
                match o["systems"] with
                | :? JsonArray as systems ->
                    systems
                    |> Seq.choose (fun s ->
                        match s with
                        | :? JsonObject as so ->
                            match so["id"] with
                            | :? JsonValue as v ->
                                match v.TryGetValue<string>() with
                                | true, id -> Some id
                                | _ -> None
                            | _ -> None
                        | _ -> None)
                    |> Set.ofSeq
                    |> Some
                | _ -> None
            | _ -> None)

let private resultDocument (capability: string) (operationId: string option) (status: string) (fields: (string * JsonNode) list) =
    obj (
        [ "schema", str ResultSchema
          "integrationVersion", str IntegrationVersion
          "capability", str capability
          "operationId", optStr operationId
          "status", str status ]
        @ fields
    )

let private writeResult (out: TextWriter) (json: bool) (document: JsonNode) (text: string) =
    if json then out.Write(Wire.render document) else out.WriteLine text

let private mutate (out: TextWriter) (stdin: TextReader) (capabilityName: string) (args: string list) =
    let json = hasFlag "--json" args
    let store = tryOption "--store" args |> Option.defaultValue (Directory.GetCurrentDirectory())

    let invalid (operationId: string option) code message =
        writeResult
            out
            json
            (resultDocument capabilityName operationId "invalid" [ "code", str code; "message", str message ])
            $"invalid: {message}"

        Exit.InvalidArguments

    match readRequest stdin capabilityName args |> Result.bind Wire.requestFromJson, readCatalog args with
    | Error e, _ -> invalid None "invalid-request" (Wire.describe e)
    | _, Error e -> invalid None "invalid-catalog" (Wire.describe e)
    | Ok request, Ok catalog ->
        match Validation.request catalog request with
        | Error e -> invalid (Some request.OperationId) (Validation.code e) (Validation.describe e)
        | Ok request ->
            match Store.apply store request with
            | Error e ->
                writeResult
                    out
                    json
                    (resultDocument capabilityName (Some request.OperationId) "failed" [ "code", str "store-failure"; "message", str (Store.describe e) ])
                    $"failed: {Store.describe e}"

                Exit.StoreFailure
            | Ok decision ->
                let current () =
                    Store.readEvents store
                    |> Result.toOption
                    |> Option.bind (Inventory.currentFor request.Target request.SystemId)
                    |> Option.map Wire.currentToJson
                    |> Option.toObj

                match decision with
                | Inventory.Record e ->
                    writeResult
                        out
                        json
                        (resultDocument
                            capabilityName
                            (Some request.OperationId)
                            "recorded"
                            [ "eventId", str e.EventId
                              "operation", str (InstallationOperation.toWire e.Operation)
                              "event", Wire.eventToJson e
                              "current", current () ])
                        $"recorded {InstallationOperation.toWire e.Operation}: {SystemId.value e.SystemId} {SemVer.text e.SystemVersion} on {Target.key e.Target} ({e.EventId})"

                    Exit.Success
                | Inventory.Replay e ->
                    writeResult
                        out
                        json
                        (resultDocument
                            capabilityName
                            (Some request.OperationId)
                            "replayed"
                            [ "eventId", str e.EventId
                              "operation", str (InstallationOperation.toWire e.Operation)
                              "current", current () ])
                        $"replayed: operation {request.OperationId} was already recorded as {e.EventId}"

                    Exit.Success
                | Inventory.Unchanged c ->
                    writeResult
                        out
                        json
                        (resultDocument capabilityName (Some request.OperationId) "unchanged" [ "current", Wire.currentToJson c ])
                        $"unchanged: {SystemId.value c.SystemId} {SemVer.text c.Version} on {Target.key c.Target} is already {InstallationState.toWire c.State}"

                    Exit.Success
                | Inventory.Refused(code, message) ->
                    writeResult
                        out
                        json
                        (resultDocument capabilityName (Some request.OperationId) "refused" [ "code", str code; "message", str message; "current", current () ])
                        $"refused: {message}"

                    Exit.Refused

let private parseFilter (args: string list) : Result<Queries.Filter, string> =
    let parseWith name (f: string -> Result<'a, ModelError>) =
        match tryOption name args with
        | None -> Ok None
        | Some raw -> f raw |> Result.map Some |> Result.mapError (fun e -> $"{name}: %A{e}")

    parseWith "--target" Target.parseKey
    |> Result.bind (fun target ->
        parseWith "--system" SystemId.create
        |> Result.bind (fun system ->
            parseWith "--version" SemVer.parse
            |> Result.bind (fun version ->
                parseWith "--older-than" SemVer.parse
                |> Result.bind (fun olderThan ->
                    match tryOption "--state" args with
                    | None -> Ok None
                    | Some s ->
                        match InstallationState.fromWire s with
                        | Some st -> Ok(Some st)
                        | None -> Error $"--state: unknown state '{s}'"
                    |> Result.map (fun state ->
                        { Queries.Target = target
                          Queries.System = system
                          Queries.Version = version
                          Queries.OlderThan = olderThan
                          Queries.State = state })))))

let private query (out: TextWriter) (args: string list) =
    let json = hasFlag "--json" args
    let store = tryOption "--store" args |> Option.defaultValue (Directory.GetCurrentDirectory())

    let now =
        tryOption "--now" args
        |> Option.bind (fun raw -> Wire.parseTimestamp raw |> Result.toOption)
        |> Option.defaultValue DateTimeOffset.UtcNow

    let maxAge = tryOption "--stale-after-days" args |> Option.bind (fun d -> match Int32.TryParse d with | true, n -> Some(TimeSpan.FromDays(float n)) | _ -> None)

    match parseFilter args, Store.readEvents store with
    | Error message, _ ->
        out.WriteLine message
        Exit.InvalidArguments
    | _, Error e ->
        out.WriteLine(Store.describe e)
        Exit.StoreFailure
    | Ok filter, Ok events ->
        let selected = Inventory.project events |> Queries.select filter

        let selected =
            match maxAge, hasFlag "--stale-only" args with
            | Some age, true -> Queries.stale now age selected
            | _ -> selected

        if json then
            let items = JsonArray()

            selected
            |> List.iter (fun c ->
                let node = Wire.currentToJson c
                node["status"] <- str (Queries.status now maxAge c)
                items.Add node)

            out.Write(Wire.render (obj [ "schema", str "echelon.installation.query-result/v1"; "installations", items ]))
        else
            selected
            |> List.groupBy (fun c -> Target.key c.Target)
            |> List.iter (fun (target, rows) ->
                out.WriteLine $"TARGET: {target}"
                out.WriteLine(String.Format("{0,-24}{1,-14}{2}", "SYSTEM", "VERSION", "STATE"))

                rows
                |> List.iter (fun c ->
                    out.WriteLine(String.Format("{0,-24}{1,-14}{2}", SystemId.value c.SystemId, SemVer.text c.Version, Queries.status now maxAge c)))

                out.WriteLine())

        Exit.Success

let private history (out: TextWriter) (args: string list) =
    let json = hasFlag "--json" args
    let store = tryOption "--store" args |> Option.defaultValue (Directory.GetCurrentDirectory())

    match tryOption "--target" args |> Option.map Target.parseKey, Store.readEvents store with
    | None, _ ->
        out.WriteLine "--target KIND:ID is required"
        Exit.InvalidArguments
    | Some(Error e), _ ->
        out.WriteLine $"--target: %A{e}"
        Exit.InvalidArguments
    | _, Error e ->
        out.WriteLine(Store.describe e)
        Exit.StoreFailure
    | Some(Ok target), Ok events ->
        let system = tryOption "--system" args |> Option.bind (SystemId.create >> Result.toOption)
        let rows = Queries.history target system events

        if json then
            let items = JsonArray()
            rows |> List.iter (Wire.eventToJson >> items.Add)
            out.Write(Wire.render (obj [ "schema", str "echelon.installation.history/v1"; "target", str (Target.key target); "events", items ]))
        else
            out.WriteLine $"HISTORY: {Target.key target}"

            rows
            |> List.iter (fun e ->
                let previous = e.PreviousVersion |> Option.map (fun v -> $" (from {SemVer.text v})") |> Option.defaultValue ""

                out.WriteLine(
                    String.Format(
                        "{0}  {1,-10}{2,-12}{3}{4}",
                        Wire.timestamp e.OccurredAt,
                        SystemId.value e.SystemId,
                        InstallationOperation.toWire e.Operation,
                        SemVer.text e.SystemVersion,
                        previous
                    )
                ))

        Exit.Success

let private project (out: TextWriter) (args: string list) =
    let store = tryOption "--store" args |> Option.defaultValue (Directory.GetCurrentDirectory())

    match Store.readEvents store with
    | Error e ->
        out.WriteLine(Store.describe e)
        Exit.StoreFailure
    | Ok events when hasFlag "--check" args ->
        if Store.projectionIsCurrent store events then
            out.WriteLine $"projection is current ({events.Length} events)"
            Exit.Success
        else
            out.WriteLine "generated/installations-current.json is stale; run `administration installation project`"
            Exit.Refused
    | Ok _ ->
        match Store.regenerate store with
        | Ok events ->
            out.WriteLine $"projection regenerated from {events.Length} events"
            Exit.Success
        | Error e ->
            out.WriteLine(Store.describe e)
            Exit.StoreFailure

let private version (out: TextWriter) (args: string list) =
    let assemblyVersion =
        Reflection.Assembly.GetExecutingAssembly().GetName().Version
        |> Option.ofObj
        |> Option.map (fun v -> $"{v.Major}.{v.Minor}.{v.Build}")
        |> Option.defaultValue "unknown"

    if hasFlag "--json" args then
        let capabilities = JsonArray()

        [ "installation.register"; "installation.verify"; "installation.remove"; "installation.reconcile"; "installation.query" ]
        |> List.iter (str >> capabilities.Add)

        out.Write(
            Wire.render (
                obj
                    [ "system", str "project-administration"
                      "executable", str "administration"
                      "version", str assemblyVersion
                      "integrationVersion", str IntegrationVersion
                      "capabilities", capabilities ]
            )
        )
    else
        out.WriteLine $"administration {assemblyVersion} (integration {IntegrationVersion})"

    Exit.Success

/// Dispatch a command line. Pure of process globals except the injected
/// reader/writer, so tests drive it directly.
let run (stdin: TextReader) (out: TextWriter) (args: string list) : int =
    match args with
    | [ "version" ]
    | "version" :: _ -> version out args
    | "installation" :: "register" :: rest -> mutate out stdin "installation.register" rest
    | "installation" :: "verify" :: rest -> mutate out stdin "installation.verify" rest
    | "installation" :: "remove" :: rest -> mutate out stdin "installation.remove" rest
    | "installation" :: "reconcile" :: rest -> mutate out stdin "installation.reconcile" rest
    | "installation" :: "query" :: rest
    | "installation" :: "list" :: rest -> query out rest
    | "installation" :: "history" :: rest -> history out rest
    | "installation" :: "project" :: rest -> project out rest
    | _ ->
        out.WriteLine usage
        Exit.InvalidArguments

[<EntryPoint>]
let main (argv: string array) =
    let version =
        Reflection.Assembly.GetExecutingAssembly().GetName().Version
        |> Option.ofObj
        |> Option.map string

    let config = Aegis.configure "Administration.Cli" version [ Sinks.console ]

    match Bootstrap.validate None config with
    | Result.Error problems ->
        for problem in problems do
            let _, message = Bootstrap.describe problem
            Console.Error.WriteLine $"Aegis configuration error: {message}"

        1
    | Ok validated ->
        let scope = Aegis.scope validated "Administration.Cli.Main" Map.empty

        let classify scope ex =
            Aegis.faultOf
                validated
                scope
                (FaultCode "ADMINISTRATION.CLI.UNHANDLED")
                UnknownFailure
                FaultSeverity.Error
                DegradedApplication
                RequiresIntervention
                ManualIntervention
                "Project Administration encountered an unexpected operational failure."
                ex

        match Aegis.capture validated scope classify (fun () -> run Console.In Console.Out (List.ofArray argv)) with
        | Ok exitCode -> exitCode
        | Result.Error fault ->
            Console.Error.WriteLine $"{fault.UserMessage} Reference {fault.Id.Value}"
            1
