using Fy.Services;
using UnityEngine;

namespace Fy.EventSystem.Examples
{
    /// <summary>
    /// Example listener: subscribes on enable, reacts to every <see cref="PlayerScoredEvent"/>, and
    /// unsubscribes on disable using the <see cref="EventHandle"/> returned at subscription.
    /// </summary>
    public sealed class ScoreListener : MonoBehaviour
    {
        private EventHandle _handle;

        private void OnEnable()
        {
            IEventService eventService = ServiceLocator.GetChecked<IEventService>();
            _handle = eventService.AddListener<PlayerScoredEvent>(HandlePlayerScored);
        }

        private void OnDisable()
        {
            // The service is gone once play mode ends and the locator resets, so guard the lookup.
            if (ServiceLocator.TryGet(out IEventService eventService))
            {
                eventService.RemoveListener(in _handle);
            }
        }

        // The event data arrives by readonly reference alongside the invocation context.
        private void HandlePlayerScored(ref EventContext context, in PlayerScoredEvent e)
        {
            Debug.Log($"[Listener] Reacted: +{e.Points} points, total is now {e.TotalScore}. " +
                      $"Sender was '{context.Sender}'.", this);
        }
    }
}
