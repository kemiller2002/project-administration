/// The receiving-system boundary as spokes see it: a typed request in, a
/// structured result out, through the `administration` executable's
/// dispatcher.
module Administration.Tests.CliTests

open System.IO
open System.Text.Json.Nodes
open Xunit
open Administration.Cli
open Administration.Tests.StoreTests

let run (args: string list) (stdin: string) =
    use out = new StringWriter()
    let code = Program.run (new StringReader(stdin)) out args
    code, out.ToString()

let field (name: string) (json: string) =
    match (JsonNode.Parse json).[name] with
    | null -> None
    | node -> Some(node.ToString())

let register store extra =
    run
        ([ "installation"; "register"; "--store"; store; "--json" ]
         @ extra
         @ [ "--operation-id"; "op-1"; "--occurred-at"; "2026-09-29T12:00:00Z"
             "--system"; "praxis"; "--version"; "3.6.0"
             "--target-kind"; "repository"; "--target-id"; "echelon-foundry/example"
             "--source-repository"; "kemiller2002/praxis"; "--distribution"; "npm" ])
        ""

[<Fact>]
let ``register, repeat and query through the executable`` () =
    let store = tempStore ()
    let code, out = register store []
    Assert.Equal(0, code)
    Assert.Equal(Some "recorded", field "status" out)
    Assert.Equal(Some "installed", field "operation" out)

    let code, out = register store []
    Assert.Equal(0, code)
    Assert.Equal(Some "replayed", field "status" out)

    let code, out = register store [ "--operation-id"; "op-2"; "--occurred-at"; "2026-09-29T13:00:00Z" ]
    Assert.Equal(0, code)
    Assert.Equal(Some "unchanged", field "status" out)

    let code, out = run [ "installation"; "query"; "--store"; store; "--target"; "repository:echelon-foundry/example" ] ""
    Assert.Equal(0, code)
    Assert.Contains("praxis", out)
    Assert.Contains("3.6.0", out)
    Assert.Contains("installed", out)

[<Fact>]
let ``a request document on stdin is the same contract`` () =
    let store = tempStore ()

    let request =
        """{ "schema": "echelon.installation.request/v1", "capability": "installation.register",
             "operationId": "op-doc", "occurredAt": "2026-09-29T12:00:00.000Z",
             "systemId": "ordo", "systemVersion": "1.4.0",
             "target": { "kind": "environment", "id": "ws-primary" },
             "source": { "repository": "kemiller2002/ordo", "distribution": "github-release", "release": "v1.4.0", "artifact": "ordo-linux-x64.tar.gz", "digest": "sha256:ab" },
             "evidence": [ { "kind": "receipt", "reference": ".conditor/ledger.json#step-3", "digest": null } ],
             "actor": { "kind": "automation", "id": "conditor", "provider": null, "model": null, "runtime": "conditor" },
             "execution": { "id": "EXE-7", "workItem": "kemiller2002/conditor#9", "repository": "kemiller2002/conditor" } }"""

    let code, out = run [ "installation"; "register"; "--store"; store; "--request"; "-"; "--json" ] request
    Assert.Equal(0, code)
    Assert.Equal(Some "recorded", field "status" out)
    Assert.Contains("EXE-7", out)

[<Fact>]
let ``malformed requests and privacy leaks are refused before writing`` () =
    let store = tempStore ()
    let code, out = register store [ "--version"; "3.6" ]
    Assert.Equal(2, code)
    Assert.Equal(Some "invalid", field "status" out)

    let code, out =
        run
            [ "installation"; "register"; "--store"; store; "--json"; "--operation-id"; "op"; "--occurred-at"; "2026-09-29T12:00:00Z"
              "--system"; "praxis"; "--version"; "3.6.0"; "--target-kind"; "environment"; "--target-id"; "ws"
              "--evidence"; "receipt=/home/kem/.conditor/ledger.json" ]
            ""

    Assert.Equal(2, code)
    Assert.Equal(Some "privacy-refused", field "code" out)
    Assert.False(Directory.Exists(Administration.Installations.Store.eventsDirectory store) && Directory.GetFiles(Administration.Installations.Store.eventsDirectory store).Length > 0)

[<Fact>]
let ``unknown systems are refused against an available catalog`` () =
    let store = tempStore ()
    let catalog = Path.Combine(store, "systems.json")
    File.WriteAllText(catalog, """{ "schema": "echelon.registry/v1", "systems": [ { "id": "ordo" } ] }""")
    let code, out = register store [ "--catalog"; catalog ]
    Assert.Equal(2, code)
    Assert.Equal(Some "unknown-system", field "code" out)

[<Fact>]
let ``project --check detects a stale projection`` () =
    let store = tempStore ()
    register store [] |> ignore
    Assert.Equal(0, fst (run [ "installation"; "project"; "--check"; "--store"; store ] ""))
    File.WriteAllText(Administration.Installations.Store.projectionPath store, "{}")
    Assert.Equal(3, fst (run [ "installation"; "project"; "--check"; "--store"; store ] ""))
    Assert.Equal(0, fst (run [ "installation"; "project"; "--store"; store ] ""))
    Assert.Equal(0, fst (run [ "installation"; "project"; "--check"; "--store"; store ] ""))
