using System;
using System.Collections.Generic;

namespace Fy.EventSystem
{
    /// <summary>
    /// Static per-event-type bucket holding the actual listener delegates, keyed by handle.
    /// </summary>
    /// <remarks>
    /// The CLR creates one <see cref="Value"/> dictionary per closed <typeparamref name="TEvent"/>, giving direct
    /// delegate storage with no boxing and no per-invoke type lookup. The state is shared across all
    /// <see cref="IEventService"/> instances; isolation comes from the Guid-unique handles, and cleanup is
    /// guaranteed by <see cref="EventSystem.Dispose"/> flushing every handle through <see cref="RemoveHandler"/>.
    /// </remarks>
    internal static class EventCallbacks<TEvent>
        where TEvent : struct, IEvent
    {
        internal static readonly Dictionary<EventHandle, EventContextHandler<TEvent>> Value = new(1);

        /// <summary>
        /// Captured non-generic bridge letting <see cref="Event"/> delete a delegate without knowing
        /// <typeparamref name="TEvent"/>.
        /// </summary>
        internal static readonly Func<EventHandle, bool> RemoveHandler = Value.Remove;
    }
}
