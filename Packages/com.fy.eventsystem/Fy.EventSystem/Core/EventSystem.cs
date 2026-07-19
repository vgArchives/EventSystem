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
    /// Listener delegates live in static per-event-type buckets shared across instances. <see cref="Dispose"/>
    /// flushes every registered handle out of those buckets — the service locator calls it on play-mode exit,
    /// which is what keeps listeners from leaking across play sessions even with domain reload disabled.
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
            EventCallbacks<TEvent>.Value.Add(handle, eventHandler);
            GetOrCreateEvent<TEvent>().Add(in handle);

            return handle;
        }

        /// <inheritdoc/>
        public bool RemoveListener(in EventHandle eventHandle)
        {
            return eventHandle.Type != null
                && _events.TryGetValue(eventHandle.Type, out Event e)
                && e.RemoveListener(in eventHandle);
        }

        /// <inheritdoc/>
        public bool RemoveAllListeners<TEvent>()
            where TEvent : struct, IEvent
        {
            return _events.TryGetValue(typeof(TEvent), out Event e) && e.RemoveAllListeners();
        }

        /// <inheritdoc/>
        public bool Invoke<TEvent>(object eventSender, in TEvent eventData)
            where TEvent : struct, IEvent
        {
            if (!_events.TryGetValue(typeof(TEvent), out Event e))
            {
                return false;
            }

            bool hasSettings = TryGetSettings(out EventSettings settings);
            bool logRecursiveInvocationWarning = !hasSettings || settings.LogRecursiveInvocationWarning;
            bool validateInvocationTargets = !hasSettings || settings.ValidateInvocationTargets;

            if (e.IsInvoking)
            {
                if (logRecursiveInvocationWarning)
                {
                    Debug.LogWarning($"{typeof(TEvent)} is already being invoked! " +
                                     $"Skipping its invocation to avoid a stack overflow.");
                }

                return false;
            }

            int listenerCount = e.ListenerCount;

            if (listenerCount == 0)
            {
                return false;
            }

            EventContext context = new(this, eventSender, typeof(TEvent));

            using (new Event.InvokeScope(e))
            {
                try
                {
                    for (int i = 0; i < listenerCount; i++)
                    {
                        context.CurrentHandle = e[i];

                        if (e.IsRemoving(in context.CurrentHandle))
                        {
                            continue;
                        }

                        EventContextHandler<TEvent> listener = EventCallbacks<TEvent>.Value[context.CurrentHandle];

                        if (validateInvocationTargets && !IsListenerAlive(listener))
                        {
                            e.RemoveListener(in context.CurrentHandle);

                            continue;
                        }

                        listener.Invoke(ref context, in eventData);
                    }
                }
                catch (Exception exception)
                {
                    Delegate listener = EventCallbacks<TEvent>.Value[context.CurrentHandle];
                    Debug.LogError($"An exception occurred while invoking {typeof(TEvent)} for " +
                                   $"{listener.Target}.{listener.Method.Name}!", eventSender as Object);
                    Debug.LogException(exception);
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public bool HasListener(in EventHandle eventHandle)
        {
            return eventHandle.Type != null
                && _events.TryGetValue(eventHandle.Type, out Event e)
                && e.HasListener(in eventHandle);
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
            return _events.TryGetValue(eventType, out Event e) ? e.ListenerCount : 0;
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
            return _events.TryGetValue(eventType, out Event e) && e.IsInvoking;
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
            if (_events.TryGetValue(typeof(TEvent), out Event e))
            {
                e.OnRelevancyChanged -= handler;
            }
        }

        /// <summary>
        /// Removes every listener of every event type. The service locator calls this on play-mode exit, flushing
        /// the static per-event-type buckets so no listener leaks into the next play session.
        /// </summary>
        public void Dispose()
        {
            foreach (Event e in _events.Values)
            {
                e.RemoveAllListeners();
            }

            _events.Clear();
        }

        private Event GetOrCreateEvent<TEvent>()
            where TEvent : struct, IEvent
        {
            if (!_events.TryGetValue(typeof(TEvent), out Event e))
            {
                e = Event.Create<TEvent>(this);
                _events.Add(typeof(TEvent), e);
            }

            return e;
        }

        private static bool TryGetSettings(out EventSettings settings)
        {
            return ScriptableSettingsRegistry.TryGet(out settings);
        }

        // A destroyed MonoBehaviour target is not C# null, so it must be checked as a Unity object.
        private static bool IsListenerAlive(Delegate listener)
        {
            if (listener.Method.IsStatic)
            {
                return true;
            }

            if (listener.Target is Object unityObject)
            {
                return unityObject != null;
            }

            return listener.Target != null;
        }
    }
}
