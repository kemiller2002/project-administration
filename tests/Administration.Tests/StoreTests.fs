module Administration.Tests.StoreTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Administration.Installations
open Administration.Installations.Model
open Administration.Tests.InventoryTests

let tempStore () =
    let dir = Path.Combine(Path.GetTempPath(), "pa-store-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    dir

let apply store r = Store.apply store r |> ok

[<Fact>]
let ``events are separate immutable files and the projection is generated`` () =
    let store = tempStore ()
    apply store (request Register ordo "1.4.0" t0 "op-1") |> ignore
    apply store (request Register ordo "1.5.0" (t0.AddDays 1.) "op-2") |> ignore

    let files = Directory.GetFiles(Store.eventsDirectory store, "*.json")
    Assert.Equal(2, files.Length)
    Assert.True(File.Exists(Store.projectionPath store))

    let events = Store.readEvents store |> ok
    Assert.True(Store.projectionIsCurrent store events)
    Assert.Contains("\"canonical\": false", File.ReadAllText(Store.projectionPath store))

[<Fact>]
let ``a deleted projection is regenerated identically from history`` () =
    let store = tempStore ()
    apply store (request Register ordo "1.4.0" t0 "op-1") |> ignore
    apply store (request Register praxis "3.6.0" t0 "op-2") |> ignore
    let before = File.ReadAllText(Store.projectionPath store)
    File.Delete(Store.projectionPath store)
    Store.regenerate store |> ok |> ignore
    Assert.Equal(before, File.ReadAllText(Store.projectionPath store))

[<Fact>]
let ``concurrent registrations never lose or overwrite an event`` () =
    let store = tempStore ()

    let requests =
        [ for i in 0..19 ->
              let system = ok (SystemId.create $"system-{i}")
              request Register system "1.0.0" (t0.AddSeconds(float i)) $"op-{i}" ]

    Parallel.ForEach(requests, fun r -> apply store r |> ignore) |> ignore

    let events = Store.readEvents store |> ok
    Assert.Equal(20, events.Length)
    Assert.True(Store.projectionIsCurrent store events)
    Assert.Equal(20, (Inventory.project events).Length)

[<Fact>]
let ``concurrent retries of one operation record exactly one event`` () =
    let store = tempStore ()
    let r = request Register praxis "3.6.0" t0 "same-op"

    let decisions = [ 1..10 ] |> List.map (fun _ -> Task.Run(fun () -> Store.apply store r)) |> List.map (fun t -> ok t.Result)

    Assert.Equal(1, Directory.GetFiles(Store.eventsDirectory store, "*.json").Length)
    Assert.Equal(1, decisions |> List.filter (fun d -> match d with Inventory.Record _ -> true | _ -> false) |> List.length)

[<Fact>]
let ``an unreadable event fails the read instead of being skipped`` () =
    let store = tempStore ()
    apply store (request Register ordo "1.4.0" t0 "op-1") |> ignore
    File.WriteAllText(Path.Combine(Store.eventsDirectory store, "IE-broken.json"), "{ not json")
    Assert.True(Result.isError (Store.readEvents store))
