using UnityEngine;

namespace Fy.EventSystem
{
    using ScriptableSettings = Fy.ScriptableSettings.ScriptableSettings;

    /// <summary>
    /// Runtime options for the <see cref="EventSystem"/>.
    /// </summary>
    /// <remarks>
    /// The asset is optional: when none is registered, the system falls back to safe defaults with both
    /// behaviors enabled.
    /// </remarks>
    public sealed class EventSettings : ScriptableSettings
    {
        [SerializeField]
        [Tooltip("Log a warning when an event is invoked from one of its own listeners?")]
        private bool _logRecursiveInvocationWarning = true;

        [SerializeField]
        [Tooltip("Validate each listener target before invoking? Invalid targets are removed automatically.")]
        private bool _validateInvocationTargets = true;

        /// <summary>
        /// Whether a warning is logged when an event is invoked from one of its own listeners.
        /// </summary>
        public bool LogRecursiveInvocationWarning => _logRecursiveInvocationWarning;

        /// <summary>
        /// Whether each listener target is validated before invoking. Listeners whose Unity target was destroyed
        /// are removed automatically when enabled.
        /// </summary>
        public bool ValidateInvocationTargets => _validateInvocationTargets;
    }
}
