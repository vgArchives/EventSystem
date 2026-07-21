using Fy.Services;
using UnityEngine;

namespace Fy.EventSystem.Examples
{
    /// <summary>
    /// Example publisher: resolves the <see cref="IEventService"/> from the <see cref="ServiceLocator"/> and
    /// invokes a <see cref="PlayerScoredEvent"/>. Click the on-screen button while in play mode to score.
    /// </summary>
    public sealed class ScorePublisher : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Points awarded per score.")]
        private int _pointsPerScore = 10;

        private int _totalScore;

        private void OnGUI()
        {
            if (!GUILayout.Button("Score!", GUILayout.Width(120f), GUILayout.Height(40f)))
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
