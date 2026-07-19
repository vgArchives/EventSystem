namespace Fy.EventSystem
{
    /// <summary>
    /// Marker interface for any event struct used with the <see cref="IEventService"/>.
    /// </summary>
    /// <remarks>
    /// Implement it on a <c>readonly struct</c> carrying the event data. The service passes the struct to listeners
    /// by readonly reference, so even large payloads are never copied per listener.
    /// </remarks>
    public interface IEvent { }
}
