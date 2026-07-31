namespace Fy.EventSystem.Examples
{
    /// <summary>
    /// Example event definition: a readonly struct implementing <see cref="IEvent"/> carrying the event data.
    /// Fired whenever the player scores.
    /// </summary>
    /// <remarks>
    /// Declared <c>partial</c> so the source generator can add the <c>AddListener</c> and <c>Invoke</c> call-site
    /// API to it. Forget the keyword and the compiler warns (FYEVT001) instead of silently skipping the type.
    /// </remarks>
    public readonly partial struct PlayerScoredEvent : IEvent
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
