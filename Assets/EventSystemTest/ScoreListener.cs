using Fy.EventSystem;
using Fy.Services;
using UnityEngine;

namespace EventSystemTest
{
    /// <summary>
    /// Point 4 of the test — listening and reacting to an event in a different class. Subscribes on enable,
    /// reacts to every <see cref="PlayerScoredEvent"/>, and unsubscribes on disable.
    /// </summary>
    public sealed class ScoreListener : MonoBehaviour
    {
        private EventHandle _handle;

        private void OnEnable()
        {
            IEventService eventService = ServiceLocator.GetChecked<IEventService>();
            _handle = eventService.AddListener<PlayerScoredEvent>(HandlePlayerScored);

            Debug.Log($"[Listener] Subscribed to PlayerScoredEvent. Handle valid: {_handle.IsValid}.", this);
        }

        private void OnDisable()
        {
            // The service is gone once play mode ends and the locator resets, so guard the lookup.
            if (ServiceLocator.TryGet(out IEventService eventService))
            {
                eventService.RemoveListener(in _handle);
                Debug.Log("[Listener] Unsubscribed from PlayerScoredEvent.", this);
            }
        }

        // Reacts to the event. The event data arrives by readonly reference alongside the invocation context.
        private void HandlePlayerScored(ref EventContext context, in PlayerScoredEvent e)
        {
            Debug.Log($"[Listener] Reacted: +{e.Points} points, total is now {e.TotalScore}. " +
                      $"Sender was '{context.Sender}'.", this);
        }
    }
}
