using Fy.EventSystem;
using Fy.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EventSystemTest
{
    /// <summary>
    /// Point 3 of the test — invoking an event from a class. Press Space to score; each press resolves the
    /// <see cref="IEventService"/> from the <see cref="ServiceLocator"/> and invokes a <see cref="PlayerScoredEvent"/>.
    /// </summary>
    public sealed class ScorePublisher : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Points awarded per key press.")]
        private int _pointsPerScore = 10;

        [SerializeField]
        [Tooltip("Key that triggers a score event.")]
        private Key _scoreKey = Key.Space;

        private int _totalScore;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard[_scoreKey].wasPressedThisFrame)
            {
                return;
            }

            _totalScore += _pointsPerScore;

            IEventService eventService = ServiceLocator.GetChecked<IEventService>();
            bool invoked = eventService.Invoke(this, new PlayerScoredEvent(_pointsPerScore, _totalScore));

            Debug.Log($"[Publisher] Scored {_pointsPerScore} (total {_totalScore}). " +
                      $"Invoke reached a listener: {invoked}.", this);
        }
    }
}
