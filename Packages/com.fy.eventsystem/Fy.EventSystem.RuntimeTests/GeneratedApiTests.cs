using Fy.Services;
using NUnit.Framework;

namespace Fy.EventSystem.RuntimeTests
{
    /// <summary>
    /// Top-level partial event used to exercise the generated call-site API. It must not be nested, because the
    /// generator only emits for top-level types.
    /// </summary>
    internal readonly partial struct GeneratedApiTestEvent : IEvent
    {
        internal readonly int Value;

        internal GeneratedApiTestEvent(int value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Verifies the source generator actually ran and its output forwards to the real service. If the generator
    /// failed to run, this fixture does not compile — which is the point.
    /// </summary>
    [TestFixture]
    public sealed class GeneratedApiTests
    {
        [TearDown]
        public void TearDown()
        {
            ServiceLocator.GetChecked<IEventService>().RemoveAllListeners<GeneratedApiTestEvent>();
        }

        /// <summary>The generated AddListener and Invoke deliver the event through the located service.</summary>
        [Test]
        public void GeneratedApi_AddListenerAndInvoke_DeliversEvent()
        {
            int receivedValue = 0;
            object receivedSender = null;

            EventHandle handle = GeneratedApiTestEvent.AddListener(
                (ref EventContext context, in GeneratedApiTestEvent e) =>
                {
                    receivedValue = e.Value;
                    receivedSender = context.Sender;
                });

            bool invoked = new GeneratedApiTestEvent(7).Invoke(this);

            Assert.That(invoked, Is.True);
            Assert.That(receivedValue, Is.EqualTo(7));
            Assert.That(receivedSender, Is.SameAs(this));

            handle.RemoveListener();
        }

        /// <summary>The generated AddListener registers on the same service the locator hands out.</summary>
        [Test]
        public void GeneratedApi_AddListener_RegistersOnLocatedService()
        {
            IEventService service = ServiceLocator.GetChecked<IEventService>();

            EventHandle handle = GeneratedApiTestEvent.AddListener(
                (ref EventContext context, in GeneratedApiTestEvent e) => { });

            Assert.That(service.GetListenerCount<GeneratedApiTestEvent>(), Is.EqualTo(1));
            Assert.That(service.HasListener(in handle), Is.True);

            handle.RemoveListener();

            Assert.That(service.GetListenerCount<GeneratedApiTestEvent>(), Is.Zero);
        }
    }
}
