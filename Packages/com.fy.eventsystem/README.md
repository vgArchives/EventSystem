# Fy Event System

Strongly-typed, allocation-light publish/subscribe for Unity, built on the Fy Service Locator.

Events are `readonly struct`s reaching listeners by readonly reference — no boxing, no garbage per broadcast.
Subscribing returns a handle you use to unsubscribe, and a source generator adds a short call-site API to each
event type so you never write the service lookup by hand.

- **Unity:** 6000.0 or newer
- **License:** MIT

## Installation

This package depends on two other Fy packages. **Unity does not resolve git dependencies automatically**, so add
all three URLs yourself, in this order — the dependencies first, or the import fails with unresolved packages.

In *Window → Package Manager → + → Install package from git URL*:

```
https://github.com/vgArchives/ServiceLocator.git?path=/Packages/com.fy.services#v0.1.0
https://github.com/vgArchives/ScriptableSettings.git?path=/Packages/com.fy.scriptablesettings#v0.1.0
https://github.com/vgArchives/EventSystem.git?path=/Packages/com.fy.eventsystem#v0.1.0
```

Or add them to `Packages/manifest.json` directly:

```json
"com.fy.services": "https://github.com/vgArchives/ServiceLocator.git?path=/Packages/com.fy.services#v0.1.0",
"com.fy.scriptablesettings": "https://github.com/vgArchives/ScriptableSettings.git?path=/Packages/com.fy.scriptablesettings#v0.1.0",
"com.fy.eventsystem": "https://github.com/vgArchives/EventSystem.git?path=/Packages/com.fy.eventsystem#v0.1.0"
```

No service registration is needed — `EventSystem` is marked `[PreloadService]`, so the `ServiceAutoLoader` from
Fy.Services discovers and registers it on load.

### Assembly references

An assembly that uses this package needs a reference to **`Fy.EventSystem` only**. The generated call-site API is
deliberately routed so that no `Fy.Services` type appears in your assembly.

## Quick start

Define an event. Mark it `partial` to get the generated API:

```csharp
public readonly partial struct PlayerScoredEvent : IEvent
{
    public readonly int Points;

    public PlayerScoredEvent(int points) => Points = points;
}
```

Subscribe, react, unsubscribe:

```csharp
private EventHandle _handle;

private void OnEnable()
{
    _handle = PlayerScoredEvent.AddListener(HandleScored);
}

private void OnDisable()
{
    _handle.RemoveListener();   // safe even once the service is gone
}

private void HandleScored(ref EventContext context, in PlayerScoredEvent eventData)
{
    Debug.Log($"Scored {eventData.Points} (sent by {context.Sender})");
}
```

Publish:

```csharp
bool reachedAListener = new PlayerScoredEvent(10).Invoke(this);
```

Subscribing to several events? Collect the handles and tear them all down at once:

```csharp
private readonly List<EventHandle> _handles = new();

private void OnDisable() => _handles.RemoveListenersAndClear();
```

## The generated call-site API

For every top-level `partial` struct implementing `IEvent`, the generator adds:

| Generated | Equivalent to |
|-----------|---------------|
| `MyEvent.AddListener(handler)` | `service.AddListener<MyEvent>(handler)` |
| `myEvent.Invoke(sender)` | `service.Invoke(sender, in myEvent)` |

Both forward to `EventService`, which resolves `IEventService` from the `ServiceLocator` on each call. Behaviour
is identical to using the service directly — it is only shorter at the call site.

Two compiler warnings keep the generator from failing silently:

- **`FYEVT001`** — the event type is not `partial`, so no API could be added.
- **`FYEVT002`** — the event type is nested or generic, which the generator does not support. Such types still
  work through the normal `IEventService` methods.

The full service API (`RemoveAllListeners`, `HasListener`, `GetListenerCount`, `IsInvoking`,
`AddRelevancyListener`) stays available on `IEventService` via `ServiceLocator.GetChecked<IEventService>()`.

## Behaviour worth knowing

- **Unsubscribing mid-broadcast is safe.** Removals during a broadcast are deferred and applied when it ends, so
  iteration is never corrupted. A listener that removes itself still runs for the broadcast in flight.
- **Recursive invocation is refused.** Invoking an event from inside its own broadcast is skipped rather than
  overflowing the stack, with a warning.
- **Destroyed Unity targets are pruned.** A listener whose `MonoBehaviour` was destroyed is removed instead of
  invoked.
- **A listener that throws does not break the broadcast.** The exception is logged with the offending target and
  method; remaining listeners still run.
- **`Dispose` clears everything.** The service locator calls it on play-mode exit, so listeners never leak into
  the next play session even with domain reload disabled.

## Settings

Create an `EventSettings` asset to tune two toggles (both default on):

- `LogRecursiveInvocationWarning` — warn when an event is invoked during its own broadcast.
- `ValidateInvocationTargets` — check each listener's Unity target is alive before invoking.

Turning validation off is faster but will call into destroyed objects, so leave it on unless a profile says
otherwise.

## Event System window

**Window → Fy → Event System** lists every event type in the project, one tab per assembly, and shows the code
that publishes and subscribes to each. It scans compiled assemblies, so it reflects the code as written rather
than the current play session — refresh after recompiling to pick up new call sites.

## Samples

Import **Event Examples** from the Package Manager for a runnable publisher/listener pair.

## Source generator

The generator source lives in `Roslyn~/` (the `~` keeps Unity from importing it) and the built DLL ships in
`Fy.EventSystem/`. To rebuild it:

```bash
cd Roslyn~/Fy.EventSystem.Roslyn
dotnet build -c Release
```

A post-build step copies the DLL into the package. See `Roslyn~/Fy.EventSystem.Roslyn/README.md` for details.
