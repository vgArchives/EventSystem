# Event Examples

Minimal working example of the Fy Event System: defining an event, publishing it, and listening to it.

## Files

| File | Shows |
|------|-------|
| `PlayerScoredEvent.cs` | Defining an event — a `readonly struct` implementing `IEvent`. |
| `ScorePublisher.cs` | Publishing — calling the generated `Invoke` from an on-screen button. |
| `ScoreListener.cs` | Listening — `AddListener` on enable, reacting in a handler, `RemoveListener` on disable. |

## How to run

1. Import this sample through **Window → Package Manager → Fy Event System → Samples**.
2. In any scene, create two GameObjects: add **ScorePublisher** to one and **ScoreListener** to the other.
3. Enter play mode and click the **Score!** button in the top-left corner.
4. Watch the Console: the publisher logs the invoke, and the listener logs its reaction with the
   event data and sender.

No manual service registration is needed — the `ServiceAutoLoader` from Fy.Services discovers and
registers the `EventSystem` automatically.

## The generated call-site API

Declaring an event `partial` lets the package's source generator add two methods to it, so you never write the
service lookup by hand:

```csharp
_handle = PlayerScoredEvent.AddListener(HandlePlayerScored);   // generated
new PlayerScoredEvent(10, 50).Invoke(this);                    // generated
_handle.RemoveListener();                                      // on EventHandle itself
```

Both forward to `ServiceLocator.GetChecked<IEventService>()` — same behaviour as calling the service directly,
just shorter. `Invoke` returns false when nobody listens, which is what the publisher logs. Two rules to know:

- **Forget `partial` and you get compiler warning `FYEVT001`** instead of silently missing methods.
- **Nested and generic event types get no generated API.** They still work perfectly through the normal
  `IEventService` methods; if you mark such a type `partial`, warning `FYEVT002` explains why nothing appeared.

## Things worth copying into real code

- Events are `readonly struct`s: passed to listeners by readonly reference, no allocation, no boxing.
- The listener stores the `EventHandle` returned by `AddListener` and hands it back in `OnDisable` —
  always pair subscribe/unsubscribe with the enable/disable lifecycle.
- Handlers should return fast: the broadcast is synchronous, so slow work belongs in a coroutine or
  async method the handler kicks off, not in the handler body itself.
