using Fy.Services;

namespace Fy.EventSystem
{
    /// <summary>
    /// Static entry point the generated call-site API forwards to, resolving the registered
    /// <see cref="IEventService"/> from the <see cref="ServiceLocator"/> on each call.
    /// </summary>
    /// <remarks>
    /// This forwards the operations instead of handing out the service, and that is the whole point. The generator
    /// emits its code into the assembly that declares the event, and <see cref="IEventService"/> derives from
    /// <c>Fy.Services.IService</c> — so an expression merely <em>typed</em> as <see cref="IEventService"/> makes the
    /// compiler demand a reference to <c>Fy.Services</c> from that assembly (CS0012). Every signature here names
    /// <c>Fy.EventSystem</c> types only, so a consumer needs a reference to this package alone.
    /// </remarks>
    public static class EventService
    {
        /// <inheritdoc cref="IEventService.AddListener{TEvent}"/>
        public static EventHandle AddListener<TEvent>(EventContextHandler<TEvent> eventHandler)
            where TEvent : struct, IEvent
        {
            return ServiceLocator.GetChecked<IEventService>().AddListener(eventHandler);
        }

        /// <inheritdoc cref="IEventService.Invoke{TEvent}"/>
        public static bool Invoke<TEvent>(object eventSender, in TEvent eventData)
            where TEvent : struct, IEvent
        {
            return ServiceLocator.GetChecked<IEventService>().Invoke(eventSender, in eventData);
        }
    }
}
