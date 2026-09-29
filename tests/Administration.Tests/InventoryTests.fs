module Administration.Tests.InventoryTests

open System
open Xunit
open Administration.Installations
open Administration.Installations.Model
open Administration.Installations.Inventory

let ok r =
    match r with
    | Ok v -> v
    | Error e -> failwithf "expected Ok, got %A" e

let t0 = DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero)
let repo = ok (Target.repository "echelon-foundry/example")
let praxis = ok (SystemId.create "praxis")
let ordo = ok (SystemId.create "ordo")

let emptySource =
    { Repository = None
      Distribution = None
      Release = None
      Artifact = None
      Digest = None }

let request capability system version (at: DateTimeOffset) opId =
    { OperationId = opId
      CorrelationId = None
      OccurredAt = at
      Capability = capability
      SystemId = system
      SystemVersion = ok (SemVer.parse version)
      Target = repo
      Source = { emptySource with Repository = Some "kemiller2002/praxis"; Digest = Some("sha256:" + version) }
      Evidence = []
      Actor = None
      Execution = None }

/// Apply requests in order against in-memory history.
let run (requests: InstallationRequest list) =
    requests
    |> List.fold
        (fun (events, decisions) r ->
            let d = decide events r

            match d with
            | Record e -> events @ [ e ], decisions @ [ d ]
            | _ -> events, decisions @ [ d ])
        ([], [])

[<Fact>]
let ``first registration records an installed event`` () =
    let events, decisions = run [ request Register praxis "3.6.0" t0 "op-1" ]

    match decisions with
    | [ Record e ] ->
        Assert.Equal(Installed, e.Operation)
        Assert.Equal(Present, e.ResultingState)
    | other -> failwithf "%A" other

    let current = project events |> List.exactlyOne
    Assert.Equal("3.6.0", current.Version.Text)
    Assert.Equal(Present, current.State)

[<Fact>]
let ``an identical registration records nothing`` () =
    let events, decisions =
        run [ request Register praxis "3.6.0" t0 "op-1"; request Register praxis "3.6.0" (t0.AddHours 1.) "op-2" ]

    Assert.Equal(1, events.Length)

    match decisions with
    | [ Record _; Unchanged c ] -> Assert.Equal(1, c.EventCount)
    | other -> failwithf "%A" other

[<Fact>]
let ``a retried operation replays rather than duplicating`` () =
    let r = request Register praxis "3.6.0" t0 "op-1"
    let events, decisions = run [ r; r ]
    Assert.Equal(1, events.Length)
    Assert.True(match decisions with [ Record _; Replay _ ] -> true | _ -> false)

[<Fact>]
let ``an upgrade preserves the earlier event and updates current state`` () =
    let events, _ =
        run [ request Register ordo "1.4.0" t0 "op-1"; request Register ordo "1.5.0" (t0.AddDays 1.) "op-2" ]

    Assert.Equal(2, events.Length)
    Assert.Equal("1.4.0", events.[0].SystemVersion.Text)
    Assert.Equal(Upgraded, events.[1].Operation)
    Assert.Equal(Some "1.4.0", events.[1].PreviousVersion |> Option.map SemVer.text)

    let current = project events |> List.exactlyOne
    Assert.Equal("1.5.0", current.Version.Text)
    Assert.Equal<string list>([ "1.4.0" ], current.PreviousVersions |> List.map SemVer.text)

[<Fact>]
let ``removal keeps history and marks the installation removed`` () =
    let events, _ =
        run [ request Register ordo "1.4.0" t0 "op-1"; request Remove ordo "1.4.0" (t0.AddDays 1.) "op-2" ]

    Assert.Equal(2, events.Length)
    let current = project events |> List.exactlyOne
    Assert.Equal(Absent, current.State)
    Assert.Equal(Some "kemiller2002/praxis", current.Source.Repository)

[<Fact>]
let ``removing twice is idempotent and removing nothing is refused`` () =
    let _, decisions =
        run
            [ request Register ordo "1.4.0" t0 "op-1"
              request Remove ordo "1.4.0" (t0.AddDays 1.) "op-2"
              request Remove ordo "1.4.0" (t0.AddDays 2.) "op-3" ]

    Assert.True(match List.last decisions with Unchanged _ -> true | _ -> false)
    Assert.True(match decide [] (request Remove ordo "1.4.0" t0 "x") with Refused("not-installed", _) -> true | _ -> false)

[<Fact>]
let ``reinstallation after removal records a new installed event`` () =
    let events, _ =
        run
            [ request Register ordo "1.4.0" t0 "op-1"
              request Remove ordo "1.4.0" (t0.AddDays 1.) "op-2"
              request Register ordo "1.4.0" (t0.AddDays 2.) "op-3" ]

    Assert.Equal(3, events.Length)
    Assert.Equal(Installed, events.[2].Operation)
    Assert.Equal(Present, (project events |> List.exactlyOne).State)

[<Fact>]
let ``the same version with a different artifact is refused, not overwritten`` () =
    let first = request Register praxis "3.6.0" t0 "op-1"
    let events, _ = run [ first ]
    let conflicting = { request Register praxis "3.6.0" (t0.AddHours 1.) "op-2" with Source = { first.Source with Digest = Some "sha256:other" } }
    Assert.True(match decide events conflicting with Refused("artifact-conflict", _) -> true | _ -> false)

[<Fact>]
let ``reconciliation can record an indeterminate state`` () =
    let events, _ =
        run [ request Register praxis "3.6.0" t0 "op-1"; request (Reconcile Indeterminate) praxis "3.6.0" (t0.AddHours 1.) "op-2" ]

    Assert.Equal(Indeterminate, (project events |> List.exactlyOne).State)

[<Fact>]
let ``source artifact, digest and execution provenance are preserved`` () =
    let r =
        { request Register praxis "3.6.0" t0 "op-1" with
            Source =
                { Repository = Some "kemiller2002/praxis"
                  Distribution = Some "npm"
                  Release = Some "v3.6.0"
                  Artifact = Some "@echelon-foundry/repository-operating-system@3.6.0"
                  Digest = Some "sha512-abc" }
            Actor = Some { Kind = Agent; Id = "anthropic/claude-code"; Provider = Some "anthropic"; Model = None; Runtime = Some "claude-code" }
            Execution = Some { ExecutionId = "EXE-1"; WorkItem = Some "kemiller2002/praxis#110"; Repository = Some "kemiller2002/praxis" } }

    let events, _ = run [ r ]
    let roundTripped = events.Head |> Wire.eventToJson |> Wire.eventFromJson |> ok
    Assert.Equal(events.Head, roundTripped)
    let current = project events |> List.exactlyOne
    Assert.Equal(Some "EXE-1", current.Execution |> Option.map (fun e -> e.ExecutionId))
    Assert.Equal(Some "sha512-abc", current.Source.Digest)

[<Fact>]
let ``the projection is independent of the order events are read in`` () =
    let events, _ =
        run
            [ request Register ordo "1.4.0" t0 "a"
              request Register praxis "3.6.0" (t0.AddMinutes 1.) "b"
              request Register ordo "1.5.0" (t0.AddDays 1.) "c"
              request Remove praxis "3.6.0" (t0.AddDays 2.) "d" ]

    Assert.Equal<CurrentInstallation list>(project events, project (List.rev events))

[<Fact>]
let ``malformed identities are refused`` () =
    Assert.True(Result.isError (SystemId.create "Praxis"))
    Assert.True(Result.isError (SystemId.create ""))
    Assert.True(Result.isError (SemVer.parse "1.4"))
    Assert.True(Result.isError (SemVer.parse "v1.4.0"))
    Assert.True(Result.isError (SemVer.parse "01.4.0"))
    Assert.True(Result.isOk (SemVer.parse "1.4.0-rc.1+build.7"))
    Assert.True(Result.isError (Target.repository "not-a-repo"))
    Assert.True(Result.isError (Target.environment "My-Laptop.local"))
    Assert.True(Result.isError (Target.environment "/home/kem"))
    Assert.True(Result.isOk (Target.environment "ws-primary"))

[<Fact>]
let ``semantic version precedence follows SemVer`` () =
    let v s = ok (SemVer.parse s)
    let ordered = [ "1.0.0-alpha"; "1.0.0-alpha.1"; "1.0.0-beta"; "1.0.0-rc.1"; "1.0.0"; "1.2.0"; "1.10.0"; "2.0.0" ]

    ordered
    |> List.pairwise
    |> List.iter (fun (a, b) -> Assert.True(SemVer.compareVersions (v a) (v b) < 0, $"{a} < {b}"))

[<Fact>]
let ``the privacy guard refuses identifying machine data`` () =
    let refused value =
        match Validation.guard "field" value with
        | Error(Validation.PrivacyRefused _) -> true
        | _ -> false

    Assert.True(refused "/home/kem/src/example")
    Assert.True(refused "C:\\Users\\kem\\src")
    Assert.True(refused "~/work")
    Assert.True(refused "kem@example.com")
    Assert.True(refused "aa:bb:cc:dd:ee:ff")
    Assert.False(refused "@echelon-foundry/repository-operating-system@3.6.0")
    Assert.False(refused "https://github.com/kemiller2002/praxis/releases/tag/v3.6.0")
    Assert.False(refused ".conditor/lock.json")

[<Fact>]
let ``an unknown system is refused only when the catalog is available`` () =
    let r = request Register (ok (SystemId.create "mystery")) "1.0.0" t0 "op"
    Assert.True(match Validation.request (Some(set [ "praxis"; "ordo" ])) r with Error(Validation.UnknownSystem _) -> true | _ -> false)
    Assert.True(Result.isOk (Validation.request None r))

[<Fact>]
let ``queries answer the inventory questions`` () =
    let ws = ok (Target.environment "ws-y")
    let at target (r: InstallationRequest) = { r with Target = target }

    let events, _ =
        run
            [ request Register ordo "1.4.0" t0 "1"
              request Register praxis "3.5.0" t0 "2"
              at ws (request Register ordo "1.4.0" t0 "3")
              at ws (request Register (ok (SystemId.create "conditor")) "0.1.0" t0 "4")
              at ws (request Register praxis "3.6.0" t0 "5")
              at ws (request Register (ok (SystemId.create "conditor")) "0.2.0" (t0.AddDays 1.) "6")
              at ws (request Remove ordo "1.4.0" (t0.AddDays 2.) "7")
              request (Reconcile Indeterminate) praxis "3.5.0" (t0.AddDays 3.) "8" ]

    let current = project events
    let q f = Queries.select f current

    Assert.Equal(2, (q { Queries.everything with Target = Some repo }).Length)

    let ordo140 = q { Queries.everything with System = Some ordo; Version = Some(ok (SemVer.parse "1.4.0")); State = Some Present }
    Assert.Equal<string list>([ "repository:echelon-foundry/example" ], ordo140 |> List.map (fun c -> Target.key c.Target))

    let older = q { Queries.everything with System = Some praxis; OlderThan = Some(ok (SemVer.parse "3.6.0")) }
    Assert.Empty(older) // the only older praxis is indeterminate, not installed

    let conditorOnY = q { Queries.everything with Target = Some ws; System = Some(ok (SystemId.create "conditor")) } |> List.exactlyOne
    Assert.Equal("0.2.0", conditorOnY.Version.Text)

    Assert.Equal(2, (Queries.history ws (Some(ok (SystemId.create "conditor"))) events).Length)
    Assert.Equal(1, (q { Queries.everything with State = Some Absent }).Length)
    Assert.Equal(1, (q { Queries.everything with State = Some Indeterminate }).Length)
    Assert.Equal(1, (Queries.stale (t0.AddDays 10.) (TimeSpan.FromDays 5.) (q { Queries.everything with Target = Some repo })).Length)
