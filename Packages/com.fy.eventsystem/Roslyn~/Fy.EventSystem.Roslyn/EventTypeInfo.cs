using System;
using Microsoft.CodeAnalysis;

namespace Fy.EventSystem.Roslyn
{
    /// <summary>
    /// Everything the generator needs to know about one event type, extracted from the semantic model.
    /// </summary>
    /// <remarks>
    /// Value equality matters: the incremental pipeline caches on it, so an unrelated edit elsewhere must not
    /// look like a change to this type.
    /// </remarks>
    internal sealed class EventTypeInfo : IEquatable<EventTypeInfo>
    {
        internal readonly string Namespace;
        internal readonly string Name;
        internal readonly string Accessibility;
        internal readonly bool IsReadOnly;
        internal readonly bool IsPartial;
        internal readonly string UnsupportedReason;
        internal readonly Location Location;

        internal EventTypeInfo(string containingNamespace, string name, string accessibility, bool isReadOnly,
            bool isPartial, string unsupportedReason, Location location)
        {
            Namespace = containingNamespace;
            Name = name;
            Accessibility = accessibility;
            IsReadOnly = isReadOnly;
            IsPartial = isPartial;
            UnsupportedReason = unsupportedReason;
            Location = location;
        }

        public bool Equals(EventTypeInfo other)
        {
            return other != null
                && Namespace == other.Namespace
                && Name == other.Name
                && Accessibility == other.Accessibility
                && IsReadOnly == other.IsReadOnly
                && IsPartial == other.IsPartial
                && UnsupportedReason == other.UnsupportedReason
                && Equals(Location, other.Location);
        }

        public override bool Equals(object obj)
        {
            return obj is EventTypeInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Namespace?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (Name?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (Accessibility?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ IsReadOnly.GetHashCode();
                hashCode = (hashCode * 397) ^ IsPartial.GetHashCode();
                hashCode = (hashCode * 397) ^ (UnsupportedReason?.GetHashCode() ?? 0);

                return hashCode;
            }
        }
    }
}
