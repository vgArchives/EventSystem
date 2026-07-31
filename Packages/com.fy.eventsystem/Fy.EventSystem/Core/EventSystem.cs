using System;
using System.Collections.Generic;
using Fy.ScriptableSettings;
using Fy.Services;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Fy.EventSystem
{
    /// <summary>
    /// Default implementation of <see cref="IEventService"/>.
    /// </summary>
    /// <remarks>
    /// Focused on <see cref="Invoke{TEvent}"/> performance with some key safety mechanisms:
    /// <list type="bullet">
    /// <item>Broadcasts are wrapped in a try/catch so a throwing listener is logged with enough information to
    /// identify it and never prevents the deferred-removal cleanup.</item>
    /// <item>Invoking an event type from one of its own listeners is a no-op (warned by default, see
    /// <see cref="EventSettings.LogRecursiveInvocationWarning"/>), preventing stack overflows by construction.</item>
    /// <item>Listener targets are validated before invocation by default (see
    /// <see cref="EventSettings.ValidateInvocationTargets"/>); listeners whose Unity target was destroyed are
    /// pruned automatically instead of invoked.</item>
    /// </list>
    /// Listener delegates live inside this instance's <see cref="Event{TEvent}"/> containers, so two services
    /// never share listener state and dropping a service drops its listeners with it.
    /// </remarks>
    [PreloadService]
    public sealed class EventSystem : IEventService
    {
        private readonly Dictionary<Type, Event> _events = new();

        /// <inheritdoc/>
        public EventHandle AddListener<TEvent>(EventContextHandler<TEvent> eventHandler)
            where TEvent : struct, IEvent
        {
            if (eventHandler == null)
            {
                Debug.LogError($"Rejected a null listener for {typeof(TEvent)}.");

                return default;
            }

            EventHandle handle = new(this, typeof(TEvent));
            GetOrCreateEvent<TEvent>().Add(in handle, eventHandler);

            return handle;
        }

        /// <inheritdoc/>
        public bool RemoveListener(in EventHandle eventHandle)
        {
            return eventHandle.Type != null
                && _events.TryGetValue(eventHandle.Type, out Event registeredEvent)
                && registeredEvent.RemoveListener(in eventHandle);
        }

        /// <inheritdoc/>
        public bool RemoveAllListeners<TEvent>()
            where TEvent : struct, IEvent
        {
            return _events.TryGetValue(typeof(TEvent), out Event registeredEvent)
                && registeredEvent.RemoveAllListeners();
        }

        /// <inheritdoc/>
        public bool Invoke<TEvent>(object eventSender, in TEvent eventData)
            where TEvent : struct, IEvent
        {
            if (!_events.TryGetValue(typeof(TEvent), out Event untypedEvent))
            {
                return false;
            }

            Event<TEvent> typedEvent = (Event<TEvent>)untypedEvent;

            if (typedEvent.IsInvoking)
            {
                if (!TryGetSettings(out EventSettings recursionSettings)
                 || recursionSettings.LogRecursiveInvocationWarning)
                {
                    Debug.LogWarning($"{typeof(TEvent)} is already being invoked! " +
                                     $"Skipping its invocation to avoid a stack overflow.");
                }

                return false;
            }

            int listenerCount = typedEvent.ListenerCount;

            if (listenerCount == 0)
            {
                return false;
            }

            bool validateInvocationTargets = !TryGetSettings(out EventSettings settings)
                                          || settings.ValidateInvocationTargets;

            EventContext context = new(this, eventSender, typeof(TEvent));
            Event<TEvent>.Listener listener = default;

            using (new Event.InvokeScope(typedEvent))
            {
                try
                {
                    for (int i = 0; i < listenerCount; i++)
                    {
                        listener = typedEvent[i];
                        context.CurrentHandle = listener.Handle;

                        if (typedEvent.IsRemoving(in listener.Handle))
                        {
                            continue;
                        }

                        if (validateInvocationTargets && !listener.IsAlive)
                        {
                            typedEvent.RemoveListener(in listener.Handle);

                            continue;
                        }

                        listener.Callback.Invoke(ref context, in eventData);
                    }
                }
                catch (Exception exception)
                {
                    Delegate faultedListener = listener.Callback;
                    Debug.LogError($"An exception occurred while invoking {typeof(TEvent)} for " +
                                   $"{faultedListener.Target}.{faultedListener.Method.Name}!", eventSender as Object);
                    Debug.LogException(exception);
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public bool HasListener(in EventHandle eventHandle)
        {
            return eventHandle.Type != null
                && _events.TryGetValue(eventHandle.Type, out Event registeredEvent)
                && registeredEvent.HasListener(in eventHandle);
        }

        /// <inheritdoc/>
        public int GetListenerCount<TEvent>()
            where TEvent : struct, IEvent
        {
            return GetListenerCount(typeof(TEvent));
        }

        /// <inheritdoc/>
        public int GetListenerCount(Type eventType)
        {
            return _events.TryGetValue(eventType, out Event registeredEvent) ? registeredEvent.ListenerCount : 0;
        }

        /// <inheritdoc/>
        public bool IsInvoking<TEvent>()
            where TEvent : struct, IEvent
        {
            return IsInvoking(typeof(TEvent));
        }

        /// <inheritdoc/>
        public bool IsInvoking(Type eventType)
        {
            return _events.TryGetValue(eventType, out Event registeredEvent) && registeredEvent.IsInvoking;
        }

        /// <inheritdoc/>
        public void AddRelevancyListener<TEvent>(EventRelevancyChangedHandler handler)
            where TEvent : struct, IEvent
        {
            GetOrCreateEvent<TEvent>().OnRelevancyChanged += handler;
        }

        /// <inheritdoc/>
        public void RemoveRelevancyListener<TEvent>(EventRelevancyChangedHandler handler)
            where TEvent : struct, IEvent
        {
            if (_events.TryGetValue(typeof(TEvent), out Event registeredEvent))
            {
                registeredEvent.OnRelevancyChanged -= handler;
            }
        }

        /// <summary>
        /// Removes every listener of every event type. The service locator calls this on play-mode exit, so a
        /// listener never survives into the next play session even with domain reload disabled.
        /// </summary>
        public void Dispose()
        {
            foreach (Event registeredEvent in _events.Values)
            {
                registeredEvent.RemoveAllListeners();
            }

            _events.Clear();
        }

        private Event<TEvent> GetOrCreateEvent<TEvent>()
            where TEvent : struct, IEvent
        {
            if (_events.TryGetValue(typeof(TEvent), out Event registeredEvent))
            {
                return (Event<TEvent>)registeredEvent;
            }

            Event<TEvent> typedEvent = new(this);
            _events.Add(typeof(TEvent), typedEvent);

            return typedEvent;
        }

        private static bool TryGetSettings(out EventSettings settings)
        {
            return ScriptableSettingsRegistry.TryGet(out settings);
        }
    }
}
