/// The repository-owned persistence layout. Only this module knows it:
///
///   installations/events/<event-id>.json      canonical, append-only
///   generated/installations-current.json      generated projection
///
/// Events are written with create-new semantics, so an existing event is
/// never overwritten. Within one checkout a lock serializes decide → write →
/// project; across checkouts (Git) concurrent writers produce distinct event
/// files that merge without conflict, and the projection is regenerated
/// deterministically from the merged history.
module Administration.Installations.Store

open System
open System.IO
open System.Threading
open Administration.Installations.Model
open Administration.Installations.Inventory

type StoreError =
    | UnreadableEvent of file: string * reason: string
    | EventIdCollision of eventId: string
    | LockUnavailable of path: string
    | IoFailure of reason: string

let eventsDirectory (root: string) = Path.Combine(root, "installations", "events")
let projectionPath (root: string) = Path.Combine(root, "generated", "installations-current.json")
let private lockPath (root: string) = Path.Combine(root, "installations", ".lock")

let private eventPath root (eventId: string) = Path.Combine(eventsDirectory root, eventId + ".json")

/// Read every canonical event. A single unreadable file fails the read rather
/// than being skipped: silently dropping history would corrupt the projection.
let readEvents (root: string) : Result<InstallationEvent list, StoreError> =
    let dir = eventsDirectory root

    if not (Directory.Exists dir) then
        Ok []
    else
        Directory.GetFiles(dir, "*.json")
        |> Array.sort
        |> Array.toList
        |> List.fold
            (fun acc file ->
                acc
                |> Result.bind (fun events ->
                    File.ReadAllText file
                    |> Wire.parse
                    |> Result.bind Wire.eventFromJson
                    |> Result.mapError (fun e -> UnreadableEvent(Path.GetFileName file, Wire.describe e))
                    |> Result.bind (fun ev ->
                        if ev.EventId + ".json" <> Path.GetFileName file then
                            Error(UnreadableEvent(Path.GetFileName file, "file name does not match eventId"))
                        else
                            Ok(ev :: events))))
            (Ok [])
        |> Result.map List.rev

let private writeAtomically (path: string) (text: string) =
    let dir = Path.GetDirectoryName path |> Option.ofObj |> Option.defaultValue "."
    Directory.CreateDirectory dir |> ignore
    let temp = Path.Combine(dir, $".{Path.GetFileName path}.{Guid.NewGuid():N}.tmp")
    File.WriteAllText(temp, text)
    File.Move(temp, path, true)

/// Regenerate the projection from canonical history.
let writeProjection (root: string) (events: InstallationEvent list) =
    let ordered = order events
    let lastId = ordered |> List.tryLast |> Option.map (fun e -> e.EventId)
    let json = Wire.projectionToJson (project events) events.Length lastId
    writeAtomically (projectionPath root) (Wire.render json)

/// Whether the checked-in projection equals what history produces.
let projectionIsCurrent (root: string) (events: InstallationEvent list) =
    let ordered = order events
    let lastId = ordered |> List.tryLast |> Option.map (fun e -> e.EventId)
    let expected = Wire.render (Wire.projectionToJson (project events) events.Length lastId)
    let path = projectionPath root
    File.Exists path && File.ReadAllText path = expected

let private withLock (root: string) (f: unit -> Result<'a, StoreError>) : Result<'a, StoreError> =
    let path = lockPath root
    Directory.CreateDirectory(Path.GetDirectoryName path |> Option.ofObj |> Option.defaultValue ".") |> ignore

    let rec acquire attempt =
        try
            Some(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        with :? IOException when attempt < 200 ->
            Thread.Sleep 25
            acquire (attempt + 1)

    match (try acquire 0 with :? IOException -> None) with
    | None -> Error(LockUnavailable path)
    | Some stream ->
        try
            f ()
        finally
            stream.Dispose()

/// Write a new event with create-new semantics. An existing file with the
/// same content is a replay; different content is a collision.
let private writeEvent root (e: InstallationEvent) : Result<unit, StoreError> =
    let path = eventPath root e.EventId
    Directory.CreateDirectory(eventsDirectory root) |> ignore
    let text = Wire.render (Wire.eventToJson e)

    try
        use stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
        use writer = new StreamWriter(stream)
        writer.Write text
        Ok()
    with :? IOException when File.Exists path ->
        if File.ReadAllText path = text then Ok() else Error(EventIdCollision e.EventId)

/// Apply a validated request: decide, record at most one event, and
/// regenerate the projection. Returns the decision.
let apply (root: string) (request: InstallationRequest) : Result<Decision, StoreError> =
    withLock root (fun () ->
        readEvents root
        |> Result.bind (fun events ->
            match decide events request with
            | Record e ->
                writeEvent root e
                |> Result.map (fun () ->
                    writeProjection root (events @ [ e ])
                    Record e)
            | other -> Ok other))

/// Regenerate the projection (for example after a Git merge).
let regenerate (root: string) =
    withLock root (fun () -> readEvents root |> Result.map (fun events -> writeProjection root events; events))

let describe (error: StoreError) =
    match error with
    | UnreadableEvent(file, reason) -> $"event {file} is unreadable: {reason}"
    | EventIdCollision id -> $"event id {id} already exists with different content"
    | LockUnavailable path -> $"could not acquire inventory lock {path}"
    | IoFailure reason -> reason
