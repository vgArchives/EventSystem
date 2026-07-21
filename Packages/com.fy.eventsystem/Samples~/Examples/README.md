# Event Examples

Minimal working example of the Fy Event System: defining an event, publishing it, and listening to it.

## Files

| File | Shows |
|------|-------|
| `PlayerScoredEvent.cs` | Defining an event — a `readonly struct` implementing `IEvent`. |
| `ScorePublisher.cs` | Publishing — resolving `IEventService` from the `ServiceLocator` and calling `Invoke`. |
| `ScoreListener.cs` | Listening — `AddListener` on enable, reacting in a handler, `RemoveListener` on disable. |

## How to run

1. Import this sample through **Window → Package Manager → Fy Event System → Samples**.
2. In any scene, create two GameObjects: add **ScorePublisher** to one and **ScoreListener** to the other.
3. Enter play mode and click the **Score!** button in the top-left corner.
4. Watch the Console: the publisher logs the invoke, and the listener logs its reaction with the
   event data and sender.

No manual service registration is needed — the `ServiceAutoLoader` from Fy.Services discovers and
registers the `EventSystem` automatically.

## Things worth copying into real code

- Events are `readonly struct`s: passed to listeners by readonly reference, no allocation, no boxing.
- The listener stores the `EventHandle` returned by `AddListener` and hands it back in `OnDisable` —
  always pair subscribe/unsubscribe with the enable/disable lifecycle.
- Handlers should return fast: the broadcast is synchronous, so slow work belongs in a coroutine or
  async method the handler kicks off, not in the handler body itself.
