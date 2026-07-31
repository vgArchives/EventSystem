using Microsoft.CodeAnalysis;

namespace Fy.EventSystem.Roslyn
{
    /// <summary>
    /// Diagnostics reported by the <see cref="EventSugarGenerator"/>. They exist because a generator is silent by
    /// nature: without them, an event type that does not qualify simply gets no generated API and no explanation.
    /// </summary>
    internal static class EventSugarDiagnostics
    {
        private const string Category = "Fy.EventSystem";

        internal static readonly DiagnosticDescriptor MissingPartialKeyword = new(
            id: "FYEVT001",
            title: "Event type should be partial",
            messageFormat: "Event type '{0}' must be declared partial to get its generated AddListener and Invoke API",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor UnsupportedEventShape = new(
            id: "FYEVT002",
            title: "Event type shape is not supported by the generator",
            messageFormat: "Event type '{0}' gets no generated API because it is {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
