using System;

namespace Fy.EventSystem
{
    /// <summary>
    /// Invocation metadata passed to every listener of an event being invoked.
    /// </summary>
    /// <remarks>
    /// A <c>ref struct</c> by design: it lives only for the duration of the callback and cannot be stored by a
    /// listener, so it never outlives the broadcast it describes.
    /// </remarks>
    public ref struct EventContext
    {
        /// <summary>
        /// The <see cref="IEventService"/> used to invoke the event.
        /// </summary>
        public readonly IEventService Service;

        /// <summary>
        /// The object that requested the event invocation.
        /// </summary>
        public readonly object Sender;

        /// <summary>
        /// The event type.
        /// </summary>
        public readonly Type Type;

        /// <summary>
        /// The handle of the listener currently being invoked. Only meant to be modified by the
        /// <see cref="IEventService"/> implementation.
        /// </summary>
        public EventHandle CurrentHandle;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventContext"/> struct. Only meant to be called from inside
        /// an <see cref="IEventService"/> implementation.
        /// </summary>
        public EventContext(IEventService service, object sender, Type type)
        {
            Service = service;
            Sender = sender;
            Type = type;
            CurrentHandle = default;
        }
    }
}
