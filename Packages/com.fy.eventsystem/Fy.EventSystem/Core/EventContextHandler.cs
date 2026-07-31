namespace Fy.EventSystem
{
    /// <summary>
    /// Listener signature for events invoked through the <see cref="IEventService"/>.
    /// </summary>
    /// <param name="context">Invocation metadata: service, sender, event type and the listener's own handle.</param>
    /// <param name="eventData">The event data, passed by readonly reference to avoid copies.</param>
    /// <typeparam name="TEvent">The event struct being listened to.</typeparam>
    public delegate void EventContextHandler<TEvent>(ref EventContext context, in TEvent eventData)
        where TEvent : struct, IEvent;
}
