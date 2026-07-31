namespace Fy.EventSystem.Editor
{
    /// <summary>
    /// One place in the codebase that publishes or subscribes to an event, found by scanning compiled assemblies.
    /// </summary>
    internal readonly struct EventCallSite
    {
        internal readonly EventCallSiteKind Kind;
        internal readonly string EventTypeName;
        internal readonly string DeclaringTypeName;
        internal readonly string MethodName;

        /// <summary>Source file of the call, or null when the assembly had no readable symbols.</summary>
        internal readonly string FilePath;

        /// <summary>Line of the call, or zero when unknown.</summary>
        internal readonly int Line;

        internal EventCallSite(EventCallSiteKind kind, string eventTypeName, string declaringTypeName,
            string methodName, string filePath, int line)
        {
            Kind = kind;
            EventTypeName = eventTypeName;
            DeclaringTypeName = declaringTypeName;
            MethodName = methodName;
            FilePath = filePath;
            Line = line;
        }

        internal bool HasSourceLocation => !string.IsNullOrEmpty(FilePath) && Line > 0;

        /// <summary>Short "Type.Method" label for the window.</summary>
        internal string DisplayName
        {
            get
            {
                int lastDot = DeclaringTypeName.LastIndexOf('.');
                string shortType = lastDot >= 0 ? DeclaringTypeName.Substring(lastDot + 1) : DeclaringTypeName;

                return $"{shortType}.{MethodName}";
            }
        }
    }
}
