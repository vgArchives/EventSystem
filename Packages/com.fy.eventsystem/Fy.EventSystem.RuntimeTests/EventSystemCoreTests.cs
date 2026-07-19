using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fy.EventSystem.RuntimeTests
{
    /// <summary>Verifies the core add/invoke/remove API: data delivery, context correctness, handles and counts.</summary>
    [TestFixture]
    [TestOf(typeof(EventSystem))]
    public sealed class EventSystemCoreTests
    {
        private readonly struct CoreTestEvent : IEvent
        {
            public readonly int Value;

            public CoreTestEvent(int value)
            {
                Value = value;
            }
        }

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

        /// <summary>A listener receives the event data and a fully populated context.</summary>
        [Test]
        public void AddListener_ThenInvoke_DeliversDataAndContext()
        {
            var sender = new object();
            int receivedValue = 0;
            object receivedSender = null;
            IEventService receivedService = null;
            Type receivedType = null;
            EventHandle receivedHandle = default;

            EventHandle handle = _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) =>
            {
                receivedValue = e.Value;
                receivedSender = context.Sender;
                receivedService = context.Service;
                receivedType = context.Type;
                receivedHandle = context.CurrentHandle;
            });

            bool invoked = _eventSystem.Invoke(sender, new CoreTestEvent(42));

            Assert.That(invoked, Is.True);
            Assert.That(receivedValue, Is.EqualTo(42));
            Assert.That(receivedSender, Is.SameAs(sender));
            Assert.That(receivedService, Is.SameAs(_eventSystem));
            Assert.That(receivedType, Is.EqualTo(typeof(CoreTestEvent)));
            Assert.That(receivedHandle, Is.EqualTo(handle));
        }

        /// <summary>Invoking an event type nobody ever listened to returns false.</summary>
        [Test]
        public void Invoke_WithoutListeners_ReturnsFalse()
        {
            bool invoked = _eventSystem.Invoke(this, new CoreTestEvent(1));

            Assert.That(invoked, Is.False);
        }

        /// <summary>Invoking after the last listener was removed returns false.</summary>
        [Test]
        public void Invoke_AfterLastListenerRemoved_ReturnsFalse()
        {
            EventHandle handle = _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => { });

            _eventSystem.RemoveListener(in handle);
            bool invoked = _eventSystem.Invoke(this, new CoreTestEvent(1));

            Assert.That(invoked, Is.False);
        }

        /// <summary>A null handler is rejected with an error and a default (invalid) handle.</summary>
        [Test]
        public void AddListener_NullHandler_LogsErrorAndReturnsDefaultHandle()
        {
            LogAssert.Expect(LogType.Error, new Regex("null listener"));
            EventHandle handle = _eventSystem.AddListener<CoreTestEvent>(null);

            Assert.That(handle.Guid, Is.EqualTo(Guid.Empty));
            Assert.That(handle.IsValid, Is.False);
        }

        /// <summary>Removing a live handle succeeds and the listener stops receiving events.</summary>
        [Test]
        public void RemoveListener_LiveHandle_ReturnsTrue_AndListenerStopsReceiving()
        {
            int callCount = 0;
            EventHandle handle = _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => callCount++);

            bool removed = _eventSystem.RemoveListener(in handle);
            _eventSystem.Invoke(this, new CoreTestEvent(1));

            Assert.That(removed, Is.True);
            Assert.That(callCount, Is.Zero);
        }

        /// <summary>Removing the same handle twice fails the second time.</summary>
        [Test]
        public void RemoveListener_StaleHandle_ReturnsFalse()
        {
            EventHandle handle = _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => { });

            _eventSystem.RemoveListener(in handle);
            bool removedAgain = _eventSystem.RemoveListener(in handle);

            Assert.That(removedAgain, Is.False);
        }

        /// <summary>A default handle is rejected.</summary>
        [Test]
        public void RemoveListener_DefaultHandle_ReturnsFalse()
        {
            EventHandle handle = default;

            Assert.That(_eventSystem.RemoveListener(in handle), Is.False);
        }

        /// <summary>A handle is rejected by a service that never saw its event type.</summary>
        [Test]
        public void RemoveListener_OnForeignService_ReturnsFalse()
        {
            var otherSystem = new EventSystem();
            EventHandle handle = _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => { });

            Assert.That(otherSystem.RemoveListener(in handle), Is.False);

            otherSystem.Dispose();
        }

        /// <summary>IsValid follows the listener lifetime: false by default, true while registered, false after removal.</summary>
        [Test]
        public void EventHandle_IsValid_TracksListenerLifetime()
        {
            EventHandle defaultHandle = default;

            Assert.That(defaultHandle.IsValid, Is.False);

            EventHandle handle = _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => { });

            Assert.That(handle.IsValid, Is.True);
            Assert.That(_eventSystem.HasListener(in handle), Is.True);

            _eventSystem.RemoveListener(in handle);

            Assert.That(handle.IsValid, Is.False);
            Assert.That(_eventSystem.HasListener(in handle), Is.False);
        }

        /// <summary>Both listener count overloads follow additions and removals.</summary>
        [Test]
        public void GetListenerCount_TracksAddAndRemove()
        {
            Assert.That(_eventSystem.GetListenerCount<CoreTestEvent>(), Is.Zero);

            EventHandle first = _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => { });
            _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => { });

            Assert.That(_eventSystem.GetListenerCount<CoreTestEvent>(), Is.EqualTo(2));
            Assert.That(_eventSystem.GetListenerCount(typeof(CoreTestEvent)), Is.EqualTo(2));

            _eventSystem.RemoveListener(in first);

            Assert.That(_eventSystem.GetListenerCount<CoreTestEvent>(), Is.EqualTo(1));
        }

        /// <summary>All listeners run in registration order.</summary>
        [Test]
        public void MultipleListeners_RunInRegistrationOrder()
        {
            var callOrder = new List<int>();

            _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => callOrder.Add(1));
            _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => callOrder.Add(2));
            _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => callOrder.Add(3));
            _eventSystem.Invoke(this, new CoreTestEvent(1));

            Assert.That(callOrder, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        /// <summary>IsInvoking is true only while a broadcast of that type is running.</summary>
        [Test]
        public void IsInvoking_TrueOnlyDuringBroadcast()
        {
            bool wasInvokingGeneric = false;
            bool wasInvokingByType = false;

            _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) =>
            {
                wasInvokingGeneric = context.Service.IsInvoking<CoreTestEvent>();
                wasInvokingByType = context.Service.IsInvoking(typeof(CoreTestEvent));
            });

            Assert.That(_eventSystem.IsInvoking<CoreTestEvent>(), Is.False);

            _eventSystem.Invoke(this, new CoreTestEvent(1));

            Assert.That(wasInvokingGeneric, Is.True);
            Assert.That(wasInvokingByType, Is.True);
            Assert.That(_eventSystem.IsInvoking<CoreTestEvent>(), Is.False);
        }

        /// <summary>Two services sharing an event type never receive each other's invocations.</summary>
        [Test]
        public void TwoServices_SameEventType_DoNotCrossTalk()
        {
            var otherSystem = new EventSystem();
            int firstCount = 0;
            int otherCount = 0;

            _eventSystem.AddListener((ref EventContext context, in CoreTestEvent e) => firstCount++);
            otherSystem.AddListener((ref EventContext context, in CoreTestEvent e) => otherCount++);

            _eventSystem.Invoke(this, new CoreTestEvent(1));

            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(otherCount, Is.Zero);

            otherSystem.Dispose();
        }
    }
}
