using System;
using Fy.Services;

namespace Fy.EventSystem
{
    /// <summary>
    /// Strongly-typed publish/subscribe service for <see cref="IEvent"/> structs.
    /// </summary>
    /// <remarks>
    /// Subscribe with <see cref="AddListener{TEvent}"/> and keep the returned <see cref="EventHandle"/> to
    /// unsubscribe later. Publish with <see cref="Invoke{TEvent}"/>; the event struct reaches every listener by
    /// readonly reference together with an <see cref="EventContext"/> describing the invocation.
    /// </remarks>
    [RequiredService]
    public interface IEventService : IService
    {
        /// <summary>
        /// Registers a listener for <typeparamref name="TEvent"/>.
        /// </summary>
        /// <param name="eventHandler">The callback to run on each invocation.</param>
        /// <returns>The handle to use with <see cref="RemoveListener"/>, or a default handle if rejected.</returns>
        EventHandle AddListener<TEvent>(EventContextHandler<TEvent> eventHandler)
            where TEvent : struct, IEvent;

        /// <summary>
        /// Removes the listener behind <paramref name="eventHandle"/>. During a broadcast of the same event type
        /// the removal is deferred until the broadcast finishes.
        /// </summary>
        /// <returns>True if the handle belonged to this service and its listener was removed or scheduled.</returns>
        bool RemoveListener(in EventHandle eventHandle);

        /// <summary>
        /// Removes all listeners of <typeparamref name="TEvent"/>.
        /// </summary>
        /// <returns>True if any listener was removed or scheduled for removal.</returns>
        bool RemoveAllListeners<TEvent>()
            where TEvent : struct, IEvent;

        /// <summary>
        /// Invokes <typeparamref name="TEvent"/> on every registered listener.
        /// </summary>
        /// <param name="eventSender">The object requesting the invocation, exposed as <see cref="EventContext.Sender"/>.</param>
        /// <param name="eventData">The event data, passed by readonly reference to each listener.</param>
        /// <returns>True if a broadcast ran; false when nobody listens or the type is already being invoked.</returns>
        bool Invoke<TEvent>(object eventSender, in TEvent eventData)
            where TEvent : struct, IEvent;

        /// <summary>
        /// Whether the listener behind <paramref name="eventHandle"/> is currently registered.
        /// </summary>
        bool HasListener(in EventHandle eventHandle);

        /// <summary>
        /// Gets the number of listeners registered for <typeparamref name="TEvent"/>.
        /// </summary>
        int GetListenerCount<TEvent>()
            where TEvent : struct, IEvent;

        /// <summary>
        /// Gets the number of listeners registered for <paramref name="eventType"/>.
        /// </summary>
        int GetListenerCount(Type eventType);

        /// <summary>
        /// Whether <typeparamref name="TEvent"/> is being invoked right now.
        /// </summary>
        bool IsInvoking<TEvent>()
            where TEvent : struct, IEvent;

        /// <summary>
        /// Whether <paramref name="eventType"/> is being invoked right now.
        /// </summary>
        bool IsInvoking(Type eventType);

        /// <summary>
        /// Registers a handler for the 0-to-1 and 1-to-0 listener count transitions of <typeparamref name="TEvent"/>.
        /// </summary>
        void AddRelevancyListener<TEvent>(EventRelevancyChangedHandler handler)
            where TEvent : struct, IEvent;

        /// <summary>
        /// Removes a handler previously registered with <see cref="AddRelevancyListener{TEvent}"/>.
        /// </summary>
        void RemoveRelevancyListener<TEvent>(EventRelevancyChangedHandler handler)
            where TEvent : struct, IEvent;
    }
}
