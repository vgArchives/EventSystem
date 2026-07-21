namespace Fy.EventSystem.Examples
{
    /// <summary>
    /// Example event definition: a readonly struct implementing <see cref="IEvent"/> carrying the event data.
    /// Fired whenever the player scores.
    /// </summary>
    public readonly struct PlayerScoredEvent : IEvent
    {
        public readonly int Points;
        public readonly int TotalScore;

        public PlayerScoredEvent(int points, int totalScore)
        {
            Points = points;
            TotalScore = totalScore;
        }
    }
}
