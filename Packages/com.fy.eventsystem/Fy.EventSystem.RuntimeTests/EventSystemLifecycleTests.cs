using System;
using Fy.Services;
using NUnit.Framework;

namespace Fy.EventSystem.RuntimeTests
{
    /// <summary>
    /// Verifies relevancy notifications, Dispose cleanup of the shared static buckets, and the zero-config
    /// registration through the service locator.
    /// </summary>
    [TestFixture]
    [TestOf(typeof(EventSystem))]
    public sealed class EventSystemLifecycleTests
    {
        private readonly struct LifecycleTestEvent : IEvent { }

        private readonly struct SecondaryLifecycleEvent : IEvent { }

        private EventSystem _eventSystem;

        [SetUp]
        public void SetUp()
        {
            _eventSystem = new EventSystem();
        }

        [TearDown]
        public void TearDown()
        {
            _eventSystem.Dispose();
        }

        /// <summary>Relevancy fires exactly on the 0-to-1 and 1-to-0 transitions, with service and type.</summary>
        [Test]
        public void RelevancyHandler_FiresOnFirstAdd_AndLastRemove()
        {
            int relevantCalls = 0;
            int irrelevantCalls = 0;
            IEventService reportedService = null;
            Type reportedType = null;

            _eventSystem.AddRelevancyListener<LifecycleTestEvent>((service, type, isRelevant) =>
            {
                reportedService = service;
                reportedType = type;

                if (isRelevant)
                {
                    relevantCalls++;
                }
                else
                {
                    irrelevantCalls++;
                }
            });

            EventHandle first = _eventSystem.AddListener((ref EventContext context, in LifecycleTestEvent e) => { });

            Assert.That(relevantCalls, Is.EqualTo(1));
            Assert.That(reportedService, Is.SameAs(_eventSystem));
            Assert.That(reportedType, Is.EqualTo(typeof(LifecycleTestEvent)));

            EventHandle second = _eventSystem.AddListener((ref EventContext context, in LifecycleTestEvent e) => { });

            Assert.That(relevantCalls, Is.EqualTo(1), "A second listener must not re-fire relevancy.");

            _eventSystem.RemoveListener(in first);

            Assert.That(irrelevantCalls, Is.Zero, "Relevancy must not fire while a listener remains.");

            _eventSystem.RemoveListener(in second);

            Assert.That(irrelevantCalls, Is.EqualTo(1));
        }

        /// <summary>RemoveAllListeners triggers a single 1-to-0 relevancy notification.</summary>
        [Test]
        public void RelevancyHandler_FiresOnce_ViaRemoveAllListeners()
        {
            int irrelevantCalls = 0;

            _eventSystem.AddRelevancyListener<LifecycleTestEvent>((service, type, isRelevant) =>
            {
                if (!isRelevant)
                {
                    irrelevantCalls++;
                }
            });

            _eventSystem.AddListener((ref EventContext context, in LifecycleTestEvent e) => { });
            _eventSystem.AddListener((ref EventContext context, in LifecycleTestEvent e) => { });
            _eventSystem.RemoveAllListeners<LifecycleTestEvent>();

            Assert.That(irrelevantCalls, Is.EqualTo(1));
        }

        /// <summary>A removed relevancy handler stops receiving notifications.</summary>
        [Test]
        public void RemoveRelevancyListener_StopsNotifications()
        {
            int calls = 0;
            EventRelevancyChangedHandler handler = (service, type, isRelevant) => calls++;

            _eventSystem.AddRelevancyListener<LifecycleTestEvent>(handler);
            _eventSystem.RemoveRelevancyListener<LifecycleTestEvent>(handler);
            _eventSystem.AddListener((ref EventContext context, in LifecycleTestEvent e) => { });

            Assert.That(calls, Is.Zero);
        }

        /// <summary>Dispose removes every listener of every type, invalidates handles and empties the static buckets.</summary>
        [Test]
        public void Dispose_ClearsAllListeners_AndStaticBuckets()
        {
            int irrelevantCalls = 0;

            _eventSystem.AddRelevancyListener<LifecycleTestEvent>((service, type, isRelevant) =>
            {
                if (!isRelevant)
                {
                    irrelevantCalls++;
                }
            });

            EventHandle first = _eventSystem.AddListener((ref EventContext context, in LifecycleTestEvent e) => { });
            EventHandle second = _eventSystem.AddListener((ref EventContext context, in SecondaryLifecycleEvent e) => { });

            Assert.That(EventCallbacks<LifecycleTestEvent>.Value.Count, Is.EqualTo(1));
            Assert.That(EventCallbacks<SecondaryLifecycleEvent>.Value.Count, Is.EqualTo(1));

            _eventSystem.Dispose();

            Assert.That(_eventSystem.GetListenerCount<LifecycleTestEvent>(), Is.Zero);
            Assert.That(_eventSystem.GetListenerCount<SecondaryLifecycleEvent>(), Is.Zero);
            Assert.That(first.IsValid, Is.False);
            Assert.That(second.IsValid, Is.False);
            Assert.That(EventCallbacks<LifecycleTestEvent>.Value.Count, Is.Zero,
                "Dispose must flush the static bucket or listeners leak across play sessions.");
            Assert.That(EventCallbacks<SecondaryLifecycleEvent>.Value.Count, Is.Zero);
            Assert.That(irrelevantCalls, Is.EqualTo(1));
        }

        /// <summary>The auto-loader registers the event service with zero manual registration code.</summary>
        [Test]
        public void ServiceLocator_ResolvesEventService_ThroughAutoLoader()
        {
            Assert.That(ServiceLocator.HasFactory<IEventService>(), Is.True,
                "ServiceAutoLoader should have registered a factory for IEventService at startup.");

            bool resolved = ServiceLocator.TryGet(out IEventService service);

            Assert.That(resolved, Is.True);
            Assert.That(service, Is.InstanceOf<EventSystem>());
        }
    }
}
