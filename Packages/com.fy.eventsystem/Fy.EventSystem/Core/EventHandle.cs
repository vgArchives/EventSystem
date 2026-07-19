using System;

namespace Fy.EventSystem
{
    /// <summary>
    /// Receipt for a registered listener, used to remove it later through the <see cref="IEventService"/>.
    /// </summary>
    /// <remarks>
    /// Equality is based on the <see cref="Guid"/> alone, so handles stay unique even across different
    /// <see cref="IEventService"/> instances sharing the same event type.
    /// </remarks>
    public readonly struct EventHandle : IEquatable<EventHandle>
    {
        /// <summary>
        /// Unique id identifying this handle.
        /// </summary>
        public readonly Guid Guid;

        /// <summary>
        /// The <see cref="IEventService"/> that generated this handle.
        /// </summary>
        public readonly IEventService Service;

        /// <summary>
        /// The event type this handle refers to.
        /// </summary>
        public readonly Type Type;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventHandle"/> struct. Only meant to be called from inside
        /// an <see cref="IEventService"/> implementation.
        /// </summary>
        public EventHandle(IEventService service, Type type)
        {
            Guid = Guid.NewGuid();
            Service = service;
            Type = type;
        }

        /// <summary>
        /// Whether this handle was ever initialized and its listener is still registered on the service
        /// that generated it.
        /// </summary>
        public bool IsValid => Guid != Guid.Empty && Service != null && Service.HasListener(in this);

        public static bool operator ==(EventHandle left, EventHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EventHandle left, EventHandle right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public bool Equals(EventHandle other)
        {
            return Guid.Equals(other.Guid);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is EventHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Guid.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Guid.ToString();
        }
    }
}
