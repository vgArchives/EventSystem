using Fy.EventSystem;

namespace EventSystemTest
{
    /// <summary>
    /// Demo event: fired whenever the player scores. Point 2 of the test — defining an event is just a
    /// readonly struct implementing <see cref="IEvent"/>.
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
