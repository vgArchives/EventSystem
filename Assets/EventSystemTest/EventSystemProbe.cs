using System;
using System.Collections;
using System.Reflection;
using Fy.EventSystem;
using Fy.Services;
using UnityEngine;

namespace EventSystemTest
{
    /// <summary>
    /// Point 1 of the test — confirms the <see cref="EventSystem"/> is initialized and running through the
    /// <see cref="ServiceLocator"/>, that it was preloaded before the first scene, and that it is a single
    /// persistent instance.
    /// </summary>
    public static class EventSystemProbe
    {
        // Runs before any scene MonoBehaviour. If [PreloadService] worked, the instance already exists here
        // without this probe (or anyone) having requested it yet.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ProbeAtBoot()
        {
            bool preloaded = WasAlreadyBuilt(typeof(IEventService));

            IEventService eventService = ServiceLocator.GetChecked<IEventService>();

            Debug.Log($"[Probe] IEventService resolved from ServiceLocator: {eventService.GetType().FullName} " +
                      $"(instance #{eventService.GetHashCode()}).");
            Debug.Log($"[Probe] Preloaded before first scene (built without being requested): {preloaded}.");
        }

        // Reads the locator's private state to check whether an instance was already built, without building one
        // ourselves (which GetChecked/TryGet would do). Test-only reflection; degrades to false if internals move.
        private static bool WasAlreadyBuilt(Type serviceInterface)
        {
            try
            {
                FieldInfo servicesField = typeof(ServiceLocator)
                    .GetField("Services", BindingFlags.NonPublic | BindingFlags.Static);

                if (servicesField?.GetValue(null) is not IDictionary services
                 || !services.Contains(serviceInterface))
                {
                    return false;
                }

                object wrapper = services[serviceInterface];
                FieldInfo valueField = wrapper.GetType().GetField("Value");

                return valueField?.GetValue(wrapper) != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Probe] Could not inspect preload state: {exception.Message}");

                return false;
            }
        }
    }
}
