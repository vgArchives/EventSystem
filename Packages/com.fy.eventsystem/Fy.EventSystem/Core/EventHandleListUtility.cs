using System.Collections.Generic;

namespace Fy.EventSystem
{
    /// <summary>
    /// Helpers for a collection of <see cref="EventHandle"/>.
    /// </summary>
    /// <remarks>
    /// Meant for a class that subscribes to several events: collect the handles in one list and tear them all down
    /// with a single call, so adding a listener never needs a matching edit in the teardown code.
    /// </remarks>
    public static class EventHandleListUtility
    {
        /// <summary>
        /// Removes the listener behind every handle in <paramref name="list"/>, then clears it.
        /// </summary>
        /// <param name="list">The tracked handles. Emptied whether or not any listener was actually removed.</param>
        /// <returns>True if at least one listener was removed.</returns>
        public static bool RemoveListenersAndClear(this IList<EventHandle> list)
        {
            bool hasRemovedAny = false;
            int count = list.Count;

            for (int i = 0; i < count; i++)
            {
                hasRemovedAny |= list[i].RemoveListener();
            }

            list.Clear();

            return hasRemovedAny;
        }
    }
}
