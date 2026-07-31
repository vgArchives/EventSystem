# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-07-31

First release.

### Added

- `IEventService` and its `EventSystem` implementation: strongly-typed publish/subscribe over `IEvent` structs,
  passed to listeners by readonly reference.
- `EventHandle` receipt returned on subscription, with `RemoveListener()` and `IsValid`.
- `RemoveListenersAndClear()` for tearing down a whole list of handles at once.
- Source generator adding a per-event call-site API — `MyEvent.AddListener(handler)` and `myEvent.Invoke(sender)` —
  to every top-level `partial` struct implementing `IEvent`, with diagnostics `FYEVT001` (missing `partial`) and
  `FYEVT002` (unsupported shape).
- Relevancy notifications on the 0-to-1 and 1-to-0 listener transitions.
- `EventSettings` asset with `LogRecursiveInvocationWarning` and `ValidateInvocationTargets`.
- **Window → Fy → Event System**: lists every event in the project and the code that publishes and subscribes to it.
- Deferred removal, so unsubscribing during a broadcast never corrupts iteration.
- Automatic pruning of listeners whose Unity target was destroyed.
- **Event Examples** sample.
