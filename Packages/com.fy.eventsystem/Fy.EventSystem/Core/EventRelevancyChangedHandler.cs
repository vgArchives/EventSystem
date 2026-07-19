using System;

namespace Fy.EventSystem
{
    /// <summary>
    /// Handler signature for event relevancy changes on the <see cref="IEventService"/>.
    /// </summary>
    /// <remarks>
    /// Fired only on the 0-to-1 (became relevant) and 1-to-0 (became irrelevant) listener count transitions.
    /// Use it to start or stop an expensive event producer only while someone is actually listening.
    /// </remarks>
    /// <param name="service">The service whose listener count changed.</param>
    /// <param name="type">The event type whose relevancy changed.</param>
    /// <param name="isRelevant">True when the first listener was added, false when the last one was removed.</param>
    public delegate void EventRelevancyChangedHandler(IEventService service, Type type, bool isRelevant);
}
