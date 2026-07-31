# Fy.EventSystem.Roslyn

Source generator that adds the per-event call-site API (`AddListener` and `Invoke`) to every `partial` struct
implementing `IEvent`.

## Why this folder is hidden

The `Roslyn~` folder name ends with `~`, so **Unity ignores it completely** — no import, no `.meta`, no
compilation. That is deliberate: this project targets `netstandard2.0` and references the Roslyn compiler API,
neither of which belongs in a Unity assembly. Unity only ever consumes the **built DLL**.

## Building

```bash
dotnet build -c Release
```

Or just build the project in Rider/Visual Studio — same MSBuild, same result.

Either way a post-build step copies `Fy.EventSystem.Roslyn.dll` into `../../Fy.EventSystem/`, where Unity picks
it up through the `RoslynAnalyzer` + `SourceGenerator` labels on its `.meta`. Refocus Unity afterwards so it
re-imports the DLL and recompiles.

> **Commit a Release build.** The copy step runs for any configuration, so a Debug build will also land in the
> package. Rebuild with `-c Release` before committing the DLL.

## Scope

Unity applies an analyzer to "that assembly, and to any other assembly that references it". Because the DLL sits
in the `Fy.EventSystem` assembly folder, it automatically covers every assembly that references the package —
which is exactly the set of assemblies that can define an `IEvent`.

## Behaviour

| Event type | Result |
|------------|--------|
| top-level `partial` struct implementing `IEvent` | gets `AddListener` + `Invoke` |
| top-level, **not** `partial` | no API, warning `FYEVT001` |
| nested or generic, **and** `partial` | no API, warning `FYEVT002` |
| nested or generic, not `partial` | no API, silent — assumed deliberate |

`AddListener` is emitted as a static member on the event struct itself; `Invoke` as an extension method on a
generated `Generated<EventName>Utility` class next to it. Both are public if the event type is public, internal
otherwise.

## Testing a change

The package's own assemblies are the test bed: `Fy.EventSystem.RuntimeTests/GeneratedApiTests.cs` calls the
generated API, so if the generator stops working that fixture stops compiling. To inspect raw output, compile any
consumer with `-generatedfilesout:<dir>` and read the emitted `.g.cs`.
