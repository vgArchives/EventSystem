using System;
using System.Collections.Generic;

namespace Fy.EventSystem
{
    /// <summary>
    /// Per-event-type container owning listener ordering and lifecycle: deferred removal while a broadcast is
    /// running, and relevancy notifications on the 0-to-1 and 1-to-0 listener transitions.
    /// </summary>
    internal sealed class Event
    {
        /// <summary>
        /// Marks the event as invoking for the scope's lifetime; disposing flushes every removal deferred during
        /// the broadcast. Listeners unsubscribing mid-broadcast therefore never corrupt the iteration.
        /// </summary>
        internal readonly ref struct InvokeScope
        {
            private readonly Event _event;

            internal InvokeScope(Event e)
            {
                _event = e;
                _event.IsInvoking = true;
            }

            internal void Dispose()
            {
                _event.IsInvoking = false;

                foreach (EventHandle handle in _event._removeSet)
                {
                    _event.RemoveListenerImmediate(in handle);
                }

                _event._removeSet.Clear();
            }
        }

        internal event EventRelevancyChangedHandler OnRelevancyChanged;

        private readonly IEventService _service;
        private readonly Type _type;
        private readonly Func<EventHandle, bool> _removeCallbackHandler;
        private readonly List<EventHandle> _listeners = new();
        private readonly HashSet<EventHandle> _removeSet = new();

        private Event(IEventService service, Type type, Func<EventHandle, bool> removeCallbackHandler)
        {
            _service = service;
            _type = type;
            _removeCallbackHandler = removeCallbackHandler;
        }

        internal EventHandle this[int index] => _listeners[index];

        internal bool IsInvoking { get; private set; }

        internal int ListenerCount => _listeners.Count;

        /// <summary>
        /// Factory capturing the typed bucket's remove bridge — the only place that knows
        /// <typeparamref name="TEvent"/>.
        /// </summary>
        internal static Event Create<TEvent>(IEventService service)
            where TEvent : struct, IEvent
        {
            return new Event(service, typeof(TEvent), EventCallbacks<TEvent>.RemoveHandler);
        }

        internal void Add(in EventHandle handle)
        {
            _listeners.Add(handle);

            if (_listeners.Count == 1)
            {
                OnRelevancyChanged?.Invoke(_service, _type, true);
            }
        }

        internal bool HasListener(in EventHandle handle)
        {
            return _listeners.Contains(handle) && !IsRemoving(in handle);
        }

        internal bool IsRemoving(in EventHandle handle)
        {
            return _removeSet.Contains(handle);
        }

        internal bool RemoveListener(in EventHandle handle)
        {
            return IsInvoking ? _removeSet.Add(handle) : RemoveListenerImmediate(in handle);
        }

        internal bool RemoveAllListeners()
        {
            bool removedAny = false;

            if (IsInvoking)
            {
                foreach (EventHandle handle in _listeners)
                {
                    removedAny |= _removeSet.Add(handle);
                }
            }
            else
            {
                foreach (EventHandle handle in _listeners)
                {
                    removedAny |= _removeCallbackHandler(handle);
                }

                _listeners.Clear();

                if (removedAny)
                {
                    OnRelevancyChanged?.Invoke(_service, _type, false);
                }
            }

            return removedAny;
        }

        private bool RemoveListenerImmediate(in EventHandle handle)
        {
            if (!_removeCallbackHandler(handle))
            {
                return false;
            }

            if (_listeners.Remove(handle) && _listeners.Count == 0)
            {
                OnRelevancyChanged?.Invoke(_service, _type, false);
            }

            return true;
        }
    }
}
