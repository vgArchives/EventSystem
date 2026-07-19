# Fy Event System — Implementation Plan (Core)

Implementation plan for the runtime core of `com.fy.eventsystem`. Scope: **all core features of the
package**. Tests, samples, and editor tooling are explicitly out of scope and listed at the end as
deferred phases.

**Reference design:** Coimbra Framework `Coimbra.Services.Events` (v11.0.5), reimplemented from
scratch on top of **Fy.Services** and **Fy.ScriptableSettings**, following our own code standards
(`A:\UnityProjects\Guidelines\ProgGuidelines.md` + `StyleGuide.cs`).

**Target:** `A:\UnityProjects\EventSystem\Packages\com.fy.eventsystem\` (scaffold already in place,
`IEventService : IService` placeholder compiles; asmdef references `Fy.Services` and
`Fy.ScriptableSettings`).

> ## ⚠ When in doubt, consult the original
>
> This design intentionally mirrors Coimbra's Event Service. If any implementation detail is unclear
> or ambiguous while executing this plan, **read the original source first** before improvising:
>
> - Runtime: `A:\UnityProjects\Kaardik\Packages\com.coimbrastudios.core@11.0.5\Coimbra.Services.Events\`
> - Locator glue: `A:\UnityProjects\Kaardik\Packages\com.coimbrastudios.core@11.0.5\Coimbra.Services\`
> - Settings reference: `...\Coimbra.Services.Events\EventSettings.cs`
> - Generators (deferred, reference only): `A:\UnityProjects\Kaardik\Packages\com.coimbrastudios.core@11.0.5\Roslyn~\`
>
> Type names map 1:1 (`Event`, `EventSystem`, `EventCallbacks<T>`, `EventHandle`, `EventContext`,
> `EventSettings`), so cross-referencing is direct.

---

## 1. Design summary (what we are building)

A strongly-typed publish/subscribe service:

- Every event is a **struct implementing `IEvent`** (marker interface).
- Subscribing returns an **`EventHandle`** (Guid receipt) used to unsubscribe.
- Publishing passes the struct **by readonly reference** (`in`) to every listener, with an
  **`EventContext`** (`ref struct`) carrying sender/service metadata.
- Listener delegates live in **per-type generic static buckets** (`EventCallbacks<TEvent>`) — no
  boxing, no per-invoke type lookup.
- A non-generic **`Event`** container per event type owns ordering + lifecycle (deferred removal
  while broadcasting, relevancy 0↔1 notifications). Same name as Coimbra's.
- **`EventSystem : IEventService`** is the engine: add/remove/invoke with a recursion guard,
  exception isolation, and dead-listener validation.
- Runtime options live in an **`EventSettings : ScriptableSettings`** asset (Fy.ScriptableSettings),
  with safe defaults when no asset exists.
- Registration is **automatic**: Fy.Services' `ServiceAutoLoader` discovers
  `EventSystem : IEventService` by reflection and registers a `DefaultServiceFactory<EventSystem>`.
  No loader code, no source generator needed.

```
caller code
   │  ServiceLocator.GetChecked<IEventService>()          (Fy.Services)
   ▼
EventSystem                       engine: invoke loop, guards, dispose
   │  Dictionary<Type, Event>            reads EventSettings (Fy.ScriptableSettings)
   ▼
Event (per event type)            ordering, IsInvoking, deferred removes, relevancy
   │  bridges via captured Func<> handlers (no generics at this layer)
   ▼
EventCallbacks<TEvent>            static Dictionary<EventHandle, EventContextHandler<TEvent>>
```

### Key decisions (deviations from Coimbra — each with rationale)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | **No Roslyn generator/analyzers** in core. Callers use `ServiceLocator` + generic API directly. | Generator is call-site sugar only; ~half-day of pipeline work with no functional gain. Revisit later. |
| D2 | Events are constrained `where TEvent : struct, IEvent`. | Enforces the value-type design (`in` passing, no allocation). Coimbra allowed classes and needed analyzers to police usage; the struct constraint gives us compile-time enforcement for free. |
| D3 | Per-type container keeps Coimbra's name **`Event`** (internal). | 1:1 naming with the reference implementation makes consulting the original trivial. It's `internal`, so the name never leaks to consumers. |
| D4 | Settings via **`EventSettings : ScriptableSettings`** (Fy.ScriptableSettings), read through `ScriptableSettingsRegistry.TryGet<EventSettings>`. **Fallback to defaults (both `true`) when no asset is registered.** | Mirrors Coimbra's `EventSettings`. One deliberate difference: Coimbra `CreateInstance`s + registers a settings object at runtime when missing, but our registry's `Set` is `internal` to its package — so the asset is optional and absence means defaults. Simpler, no runtime asset creation. |
| D5 | **No `DelegateListener` / listener-introspection API** in core. Core keeps `GetListenerCount`, `HasListener`, `IsInvoking`. | Introspection exists purely to feed an editor debug window — deferred with the window itself. Keeps `IEventService` small. |
| D6 | **No relevancy feature removal** — relevancy listeners ARE core. | It's the hook for "start/stop expensive producer only while someone listens"; cheap to implement inside `Event`. |
| D7 | `[RequiredService]` on `IEventService`; **no** `[PreloadService]` on `EventSystem`. | Lazy creation on first use is fine; the auto-loader validates required services at startup anyway. |

---

## 2. File layout

```
Fy.EventSystem\
├── AssemblyInfo.cs                            (exists)
├── Fy.EventSystem.asmdef                      (exists; references Fy.Services + Fy.ScriptableSettings)
└── Core\
    ├── IEvent.cs
    ├── IEventService.cs                       (replace placeholder)
    ├── EventHandle.cs
    ├── EventContext.cs
    ├── EventContextHandler.cs
    ├── EventRelevancyChangedHandler.cs
    ├── EventCallbacks.cs
    ├── Event.cs
    ├── EventSettings.cs
    └── EventSystem.cs
```

One type per file, `Fy.EventSystem` namespace, member order per guidelines
(Events → Fields → Properties → Unity methods → public → internal → private).

---

## 3. Phase 1 — Contracts and value types

> Goal: the full public surface compiles. No behavior yet.

### 3.1 `IEvent.cs`

```csharp
namespace Fy.EventSystem
{
    /// <summary>
    /// Marker interface for any event struct used with the <see cref="IEventService"/>.
    /// </summary>
    public interface IEvent { }
}
```

### 3.2 `EventContextHandler.cs`

```csharp
public delegate void EventContextHandler<TEvent>(ref EventContext context, in TEvent e)
    where TEvent : struct, IEvent;
```

`in TEvent` avoids copying potentially large event structs per listener.

### 3.3 `EventRelevancyChangedHandler.cs`

```csharp
public delegate void EventRelevancyChangedHandler(IEventService service, Type type, bool isRelevant);
```

Fired on the 0→1 (relevant) and 1→0 (irrelevant) listener transitions.

### 3.4 `EventHandle.cs`

`readonly struct`, `IEquatable<EventHandle>`:

- Fields: `public readonly Guid Guid;` `public readonly IEventService Service;`
  `public readonly Type Type;`
- Ctor `EventHandle(IEventService service, Type type)` → `Guid.NewGuid()`.
- `public bool IsValid => Guid != Guid.Empty && Service != null && Service.HasListener(in this);`
- Equality/hash **on Guid only**; `==`/`!=` operators; `ToString()` → Guid.

### 3.5 `EventContext.cs`

`ref struct` (stack-only by design — cannot be stored by a listener):

- `public readonly IEventService Service;` `public readonly object Sender;`
  `public readonly Type Type;`
- `public EventHandle CurrentHandle;` (mutated by the engine per listener)
- Ctor sets the three readonly fields, `CurrentHandle = default`.

### 3.6 `IEventService.cs` (final API — replaces placeholder)

```csharp
[RequiredService]
public interface IEventService : IService
{
    EventHandle AddListener<TEvent>(EventContextHandler<TEvent> eventHandler)
        where TEvent : struct, IEvent;

    bool RemoveListener(in EventHandle eventHandle);

    bool RemoveAllListeners<TEvent>()
        where TEvent : struct, IEvent;

    bool Invoke<TEvent>(object eventSender, in TEvent eventData)
        where TEvent : struct, IEvent;

    bool HasListener(in EventHandle eventHandle);

    int GetListenerCount<TEvent>()
        where TEvent : struct, IEvent;

    int GetListenerCount(Type eventType);

    bool IsInvoking<TEvent>()
        where TEvent : struct, IEvent;

    bool IsInvoking(Type eventType);

    void AddRelevancyListener<TEvent>(EventRelevancyChangedHandler handler)
        where TEvent : struct, IEvent;

    void RemoveRelevancyListener<TEvent>(EventRelevancyChangedHandler handler)
        where TEvent : struct, IEvent;
}
```

Trimmed vs Coimbra: no `GetListeners`/`GetRelevancyListeners` overloads (D5). Delegates passed
plainly (no `in` on reference types).

**Checkpoint:** package compiles; auto-loader still resolves `IEventService` (placeholder
`EventSystem` can be an empty stub implementing the interface with `NotImplementedException`, or
land Phase 1–3 in one commit).

---

## 4. Phase 2 — Storage layer

> Goal: listener storage with safe mutation semantics. Everything here is `internal`.

### 4.1 `EventCallbacks.cs` — the generic static bucket

```csharp
internal static class EventCallbacks<TEvent>
    where TEvent : struct, IEvent
{
    internal static readonly Dictionary<EventHandle, EventContextHandler<TEvent>> Value = new(1);

    internal static readonly Func<EventHandle, bool> RemoveHandler = Value.Remove;
}
```

- One dictionary per closed `TEvent` — the CLR does this for us.
- `RemoveHandler` is the captured non-generic bridge: `Event` can delete a delegate without
  knowing `TEvent`.
- **Gotcha carried from Coimbra:** static state shared across `EventSystem` instances; isolation
  comes from Guid-unique handles. Cleanup is guaranteed via `EventSystem.Dispose()` →
  `RemoveAllListeners()` → captured `RemoveHandler` per handle (see 5.6).

### 4.2 `Event.cs` — per-type ordering + lifecycle

Same name and role as Coimbra's `Event` (`Coimbra.Services.Events\Event.cs`) — consult it directly
when unsure. State:

```csharp
internal sealed class Event
{
    internal event EventRelevancyChangedHandler OnRelevancyChanged;

    private readonly IEventService _service;
    private readonly Type _type;
    private readonly Func<EventHandle, bool> _removeCallbackHandler;   // EventCallbacks<T>.RemoveHandler
    private readonly List<EventHandle> _listeners = new();
    private readonly HashSet<EventHandle> _removeSet = new();
}
```

Members (all `internal`):

| Member | Behavior |
|--------|----------|
| `static Create<TEvent>(IEventService)` | Factory; captures `EventCallbacks<TEvent>.RemoveHandler`. Only place that knows `TEvent`. |
| `this[int index]` | `_listeners[index]` — engine iterates by index. |
| `IsInvoking { get; private set; }` | True while a broadcast runs. |
| `ListenerCount` | `_listeners.Count`. |
| `Add(in EventHandle)` | Append; if count became 1 → `OnRelevancyChanged?.Invoke(_service, _type, true)`. |
| `HasListener(in EventHandle)` | `_listeners.Contains(handle) && !IsRemoving(handle)`. |
| `IsRemoving(in EventHandle)` | `_removeSet.Contains(handle)`. |
| `RemoveListener(in EventHandle)` | **If invoking:** defer → `_removeSet.Add(handle)`. Else remove immediately (below). |
| `RemoveAllListeners()` | If invoking: defer all into `_removeSet`. Else: run `_removeCallbackHandler` per handle, clear `_listeners`, fire relevancy `false` if anything removed. |
| `InvokeScope` (nested `ref struct`) | Ctor: `IsInvoking = true`. `Dispose()`: `IsInvoking = false`, flush `_removeSet` through the immediate-removal path, clear the set. |

Immediate removal path (`private RemoveListenerImmediate`, Coimbra: `RemoveListenerUnsafe`):

1. `_removeCallbackHandler(handle)` — delete the delegate from the static bucket; false → not ours,
   return false.
2. `_listeners.Remove(handle)`; if list is now empty → relevancy `false`.

**This deferred-removal mechanism is the core correctness feature** — a listener unsubscribing
itself (or others) mid-broadcast must not corrupt the iteration. Port it faithfully.

---

## 5. Phase 3 — Settings and the engine (`EventSystem`)

> Goal: full `IEventService` implementation plus its settings asset.

### 5.1 `EventSettings.cs` — runtime options (mirrors Coimbra's `EventSettings`)

```csharp
public sealed class EventSettings : ScriptableSettings
{
    [SerializeField]
    [Tooltip("Log a warning when an event is invoked from one of its own listeners?")]
    private bool _logRecursiveInvocationWarning = true;

    [SerializeField]
    [Tooltip("Validate each listener target before invoking? Invalid targets are removed automatically.")]
    private bool _validateInvocationTargets = true;

    public bool LogRecursiveInvocationWarning => _logRecursiveInvocationWarning;

    public bool ValidateInvocationTargets => _validateInvocationTargets;
}
```

`EventSystem` reads it per invocation through a private helper:

```csharp
private static bool TryGetSettings(out EventSettings settings)
{
    return ScriptableSettingsRegistry.TryGet(out settings);
}
```

- Asset present (created in `Assets/Settings/`, preloaded per Fy.ScriptableSettings rules) → its
  values win.
- No asset → **defaults: both behaviors on** (warn on recursion, validate targets). See D4 for why
  we don't create one at runtime like Coimbra does.

### 5.2 `EventSystem.cs` — skeleton

```csharp
public sealed class EventSystem : IEventService
{
    private readonly Dictionary<Type, Event> _events = new();
}
```

Public parameterless constructor (implicit) — required so `DefaultServiceFactory<EventSystem>`
applies (see Phase 4).

### 5.3 `AddListener<TEvent>`

1. Reject a null delegate (return `default` handle, log error).
2. `EventHandle handle = new(this, typeof(TEvent));`
3. `EventCallbacks<TEvent>.Value.Add(handle, eventHandler);`
4. Get-or-create the container: `_events.TryGetValue` else `Event.Create<TEvent>(this)` + add.
5. `e.Add(in handle); return handle;`

### 5.4 `Invoke<TEvent>(object sender, in TEvent data)`

Guard ladder, then loop:

1. No `Event` for `typeof(TEvent)` → return false (nobody ever listened).
2. `e.IsInvoking` → **recursion guard**: warn if `LogRecursiveInvocationWarning` (settings or
   default), return false. Prevents stack overflow by construction.
3. `ListenerCount == 0` → return false.
4. Build `EventContext context = new(this, sender, typeof(TEvent));`
5. `using (new Event.InvokeScope(e))` wrap a `try/catch`:
   - Loop `for i in 0..listenerCount` (count captured **before** the loop — listeners added
     during broadcast run next time):
     - `context.CurrentHandle = e[i];`
     - Skip if `e.IsRemoving(context.CurrentHandle)`.
     - If `ValidateInvocationTargets` (settings or default): fetch delegate, and if its target is a
       destroyed `UnityEngine.Object` → `e.RemoveListener(...)` instead of calling (see 5.5).
     - Else invoke: `EventCallbacks<TEvent>.Value[context.CurrentHandle](ref context, in data);`
   - `catch (Exception exception)`: log which listener failed (delegate `Target`/`Method` from
     `EventCallbacks<TEvent>.Value[context.CurrentHandle]`, sender as Unity context object), then
     `Debug.LogException`. The scope's `Dispose` still flushes deferred removals.
6. Return true.

### 5.5 Remaining API (thin forwards to the `Event` container)

- `RemoveListener(in handle)` → `handle.Type != null && _events.TryGetValue(...) && e.RemoveListener(in handle)`.
- `RemoveAllListeners<TEvent>()` → lookup + `e.RemoveAllListeners()`.
- `HasListener(in handle)` → lookup + `e.HasListener(in handle)`.
- `GetListenerCount` / `IsInvoking` (both generic and `Type` overloads) → dictionary lookups.
- `AddRelevancyListener<TEvent>` → get-or-create container, `e.OnRelevancyChanged += handler`.
- `RemoveRelevancyListener<TEvent>` → if container exists, `-= handler`.

### 5.6 Dead-target validation helper

Delegates hold `object Target`; a destroyed MonoBehaviour is not C# null. Private helper in
`EventSystem` (mirrors the reasoning in Fy.Services' `ObjectUtility`, which we can't reuse — it
takes `IService`):

```csharp
private static bool IsListenerAlive(Delegate listener)
{
    if (listener.Method.IsStatic)
    {
        return true;
    }

    if (listener.Target is UnityEngine.Object unityObject)
    {
        return unityObject != null;
    }

    return listener.Target != null;
}
```

### 5.7 `Dispose()` (from `IService : IDisposable`)

```csharp
public void Dispose()
{
    foreach (Event e in _events.Values)
    {
        e.RemoveAllListeners();   // flushes EventCallbacks<T> via captured RemoveHandler
    }

    _events.Clear();
}
```

Called by `ServiceLocator.Reset()` on play-mode exit — this is what keeps the static
`EventCallbacks<T>` dictionaries from leaking across play sessions and tests. **This is the answer
to the static-state gotcha**; document it in the XML docs.

---

## 6. Phase 4 — Fy.Services integration & lifecycle (verification, ~no code)

> Goal: prove zero-config registration and clean teardown.

1. **Auto-registration:** `ServiceAutoLoader.AutoRegisterAll` scans assemblies referencing
   `Fy.Services`; `Fy.EventSystem` references it, so it finds
   `EventSystem` → interface `IEventService` (not `[AbstractService]`) → registers
   `DefaultServiceFactory<EventSystem>`. Requirements already met: public parameterless ctor, not a
   MonoBehaviour, single concrete service interface. **Nothing to write.**
2. **Required-service validation:** `[RequiredService]` on `IEventService` makes the auto-loader
   error at startup if the factory is missing, and legitimizes `GetChecked<IEventService>()`.
3. **Settings asset (optional):** create an `EventSettings` asset in the dev project's
   `Assets/Settings/` and confirm the Fy.ScriptableSettings preload flow registers it; also confirm
   the system behaves correctly **without** the asset (defaults path).
4. **Lifecycle sanity pass (manual, in the dev project):**
   - Enter play mode → `ServiceLocator.GetChecked<IEventService>()` returns an `EventSystem`.
   - Add listener → invoke → callback fires with correct `Sender` and event data.
   - Exit play mode → locator `Reset()` → `Dispose()` → re-enter play → no stale listeners fire
     (validates the static-bucket cleanup, including with **domain reload disabled**).

---

## 7. Canonical usage (what the API looks like when done)

```csharp
public readonly struct PlayerDiedEvent : IEvent
{
    public readonly int Score;

    public PlayerDiedEvent(int score)
    {
        Score = score;
    }
}

// subscribe
EventHandle handle = ServiceLocator.GetChecked<IEventService>()
    .AddListener<PlayerDiedEvent>(OnPlayerDied);

private void OnPlayerDied(ref EventContext context, in PlayerDiedEvent e)
{
    Debug.Log($"{context.Sender} reported death with score {e.Score}");
}

// publish
ServiceLocator.GetChecked<IEventService>()
    .Invoke(this, new PlayerDiedEvent(score: 10));

// unsubscribe
ServiceLocator.GetChecked<IEventService>().RemoveListener(in handle);
```

---

## 8. Acceptance criteria (core complete when…)

- [ ] All ten core files exist, compile, and follow the style guide (member order, `_camelCase`,
      braces on new lines, no regions, XML docs on public members).
- [ ] `AddListener` → `Invoke` → listener receives correct `EventContext` (service, sender, type,
      current handle) and event data.
- [ ] `RemoveListener` with a live handle returns true; with a foreign/stale handle returns false;
      `handle.IsValid` reflects it.
- [ ] A listener that removes itself (or another listener) **during** a broadcast neither crashes
      nor skips unrelated listeners; the removal takes effect right after the broadcast.
- [ ] Re-invoking the same event type from inside its own broadcast is a no-op (+ warning per
      settings).
- [ ] A listener that throws does not prevent later cleanup, and the error log identifies the
      failing target/method.
- [ ] A listener whose Unity target was destroyed is skipped and pruned when
      `ValidateInvocationTargets` is on.
- [ ] `EventSettings` asset values are honored when present; defaults (both on) apply when absent.
- [ ] Relevancy handler fires exactly on 0→1 and 1→0 transitions (including via
      `RemoveAllListeners` and `Dispose`).
- [ ] Play-mode exit fully clears state; second play session starts clean (with and without domain
      reload).
- [ ] Zero manual registration code anywhere — the auto-loader wires everything.

---

## 9. Deferred (explicitly out of scope for this plan)

| Item | Trigger to build it |
|------|---------------------|
| Runtime tests (`Fy.EventSystem.RuntimeTests`) | Immediately after core — mirror the acceptance criteria above. |
| Samples (`Samples~/Examples`) | After tests. |
| `DelegateListener` + `GetListeners` introspection API | When building the editor debug window. |
| Editor debug window (pure C# UI Toolkit, per Fy.Services window pattern) | When debugging real usage gets painful. |
| `EventHandleTracker` MonoBehaviour (auto-remove listeners on destroy) | First real game integration. |
| Roslyn source generator (`PlayerDiedEvent.AddListener(...)` sugar) | Only if call-site verbosity actually annoys us; prefer `IIncrementalGenerator` if built. |
