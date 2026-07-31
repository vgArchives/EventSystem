using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace Fy.EventSystem
{
    /// <summary>
    /// Type-erased half of the per-event-type container: everything the <see cref="EventSystem"/> can do without
    /// knowing the event type, so every event type fits in one <see cref="Dictionary{TKey,TValue}"/>.
    /// </summary>
    /// <remarks>
    /// The listeners themselves live in <see cref="Event{TEvent}"/>, which is the only part that knows the event
    /// type and therefore the only part that can hold an <see cref="EventContextHandler{TEvent}"/>.
    /// </remarks>
    internal abstract class Event
    {
        /// <summary>
        /// Marks the event as invoking for the scope's lifetime; disposing compacts every removal deferred during
        /// the broadcast. Listeners unsubscribing mid-broadcast therefore never corrupt the iteration.
        /// </summary>
        internal readonly ref struct InvokeScope
        {
            private readonly Event _event;

            internal InvokeScope(Event invokingEvent)
            {
                _event = invokingEvent;
                _event.IsInvoking = true;
            }

            internal void Dispose()
            {
                _event.IsInvoking = false;

                if (_event._pendingRemovals.Count == 0)
                {
                    return;
                }

                _event.FlushRemovals();
                _event._pendingRemovals.Clear();
            }
        }

        internal event EventRelevancyChangedHandler OnRelevancyChanged;

        private readonly IEventService _service;
        private readonly Type _type;

        private protected readonly HashSet<EventHandle> _pendingRemovals = new();

        private protected Event(IEventService service, Type type)
        {
            _service = service;
            _type = type;
        }

        internal bool IsInvoking { get; private set; }

        internal abstract int ListenerCount { get; }

        /// <summary>
        /// Whether <paramref name="handle"/> is scheduled for removal by the running broadcast.
        /// </summary>
        /// <remarks>
        /// The count guard is what keeps this off the hot path: in a broadcast where nobody unsubscribes — the
        /// overwhelming majority — the question costs an integer compare instead of hashing a <see cref="Guid"/>.
        /// </remarks>
        internal bool IsRemoving(in EventHandle handle)
        {
            return _pendingRemovals.Count > 0 && _pendingRemovals.Contains(handle);
        }

        internal abstract bool HasListener(in EventHandle handle);

        internal bool RemoveListener(in EventHandle handle)
        {
            return IsInvoking ? _pendingRemovals.Add(handle) : RemoveImmediate(in handle);
        }

        internal abstract bool RemoveAllListeners();

        private protected void NotifyRelevancyChanged(bool isRelevant)
        {
            OnRelevancyChanged?.Invoke(_service, _type, isRelevant);
        }

        private protected abstract bool RemoveImmediate(in EventHandle handle);

        /// <summary>
        /// Drops every listener scheduled for removal in one compaction pass, preserving registration order.
        /// </summary>
        private protected abstract void FlushRemovals();
    }

    /// <summary>
    /// Typed half of the per-event-type container: owns the listener list, its ordering and its lifecycle.
    /// </summary>
    /// <remarks>
    /// Handle, delegate and cached Unity target live together in one <see cref="Listener"/> entry, so a broadcast
    /// reaches a listener through a sequential list access instead of a lookup keyed by <see cref="EventHandle"/>.
    /// </remarks>
    internal sealed class Event<TEvent> : Event
        where TEvent : struct, IEvent
    {
        /// <summary>
        /// One subscription: everything a broadcast needs about a listener, in a single entry.
        /// </summary>
        internal readonly struct Listener
        {
            internal readonly EventHandle Handle;
            internal readonly EventContextHandler<TEvent> Callback;

            private readonly Object _unityTarget;

            internal Listener(in EventHandle handle, EventContextHandler<TEvent> callback)
            {
                Handle = handle;
                Callback = callback;
                _unityTarget = callback.Target as Object;
            }

            /// <summary>
            /// Whether this listener may still be invoked.
            /// </summary>
            /// <remarks>
            /// The two null checks ask different questions. <see cref="object.ReferenceEquals"/> asks the plain C#
            /// one — was there a Unity target at all? — because Unity's <c>==</c> override would instead answer
            /// whether it was destroyed, which is the second check.
            /// </remarks>
            internal bool IsAlive => ReferenceEquals(_unityTarget, null) || _unityTarget != null;
        }

        private readonly List<Listener> _listeners = new();

        internal Event(IEventService service)
            : base(service, typeof(TEvent))
        {
        }

        internal override int ListenerCount => _listeners.Count;

        internal Listener this[int index] => _listeners[index];

        internal void Add(in EventHandle handle, EventContextHandler<TEvent> callback)
        {
            _listeners.Add(new Listener(in handle, callback));

            if (_listeners.Count == 1)
            {
                NotifyRelevancyChanged(true);
            }
        }

        internal override bool HasListener(in EventHandle handle)
        {
            return IndexOf(in handle) >= 0 && !IsRemoving(in handle);
        }

        internal override bool RemoveAllListeners()
        {
            if (_listeners.Count == 0)
            {
                return false;
            }

            if (IsInvoking)
            {
                bool scheduledAny = false;

                foreach (Listener listener in _listeners)
                {
                    scheduledAny |= _pendingRemovals.Add(listener.Handle);
                }

                return scheduledAny;
            }

            _listeners.Clear();
            NotifyRelevancyChanged(false);

            return true;
        }

        private protected override bool RemoveImmediate(in EventHandle handle)
        {
            int index = IndexOf(in handle);

            if (index < 0)
            {
                return false;
            }

            _listeners.RemoveAt(index);

            if (_listeners.Count == 0)
            {
                NotifyRelevancyChanged(false);
            }

            return true;
        }

        private protected override void FlushRemovals()
        {
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < _listeners.Count; readIndex++)
            {
                Listener listener = _listeners[readIndex];

                if (IsRemoving(in listener.Handle))
                {
                    continue;
                }

                _listeners[writeIndex++] = listener;
            }

            if (writeIndex == _listeners.Count)
            {
                return;
            }

            _listeners.RemoveRange(writeIndex, _listeners.Count - writeIndex);

            if (_listeners.Count == 0)
            {
                NotifyRelevancyChanged(false);
            }
        }

        private int IndexOf(in EventHandle handle)
        {
            for (int i = 0; i < _listeners.Count; i++)
            {
                if (_listeners[i].Handle == handle)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
