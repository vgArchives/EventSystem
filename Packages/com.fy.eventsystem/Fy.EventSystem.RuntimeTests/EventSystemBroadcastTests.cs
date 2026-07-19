using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Fy.ScriptableSettings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Fy.EventSystem.RuntimeTests
{
    /// <summary>
    /// Verifies broadcast correctness: mid-broadcast mutation, the recursion guard, exception isolation,
    /// dead-target pruning and how <see cref="EventSettings"/> alters those behaviors.
    /// </summary>
    [TestFixture]
    [TestOf(typeof(EventSystem))]
    public sealed class EventSystemBroadcastTests
    {
        private readonly struct BroadcastTestEvent : IEvent { }

        private sealed class UnityTargetListener : ScriptableObject
        {
            public int CallCount { get; private set; }

            public void Handle(ref EventContext context, in BroadcastTestEvent e)
            {
                CallCount++;
            }
        }

        private EventSystem _eventSystem;
        private EventSettings _createdSettings;
        private EventSettings _mutatedSettings;
        private bool _originalLogRecursiveInvocationWarning;
        private bool _originalValidateInvocationTargets;

        [SetUp]
        public void SetUp()
        {
            _eventSystem = new EventSystem();
        }

        [TearDown]
        public void TearDown()
        {
            _eventSystem.Dispose();

            if (_mutatedSettings != null)
            {
                SetSettingsFields(_mutatedSettings, _originalLogRecursiveInvocationWarning,
                    _originalValidateInvocationTargets);
                _mutatedSettings = null;
            }

            if (_createdSettings != null)
            {
                Object.DestroyImmediate(_createdSettings);
                _createdSettings = null;
            }
        }

        /// <summary>A listener that removes itself still runs this broadcast; the removal lands right after it.</summary>
        [Test]
        public void ListenerRemovingItself_RunsThisBroadcast_ThenIsRemoved()
        {
            int selfRemovingCalls = 0;
            int otherCalls = 0;
            EventHandle selfHandle = default;

            selfHandle = _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) =>
            {
                selfRemovingCalls++;
                context.Service.RemoveListener(in selfHandle);
            });
            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) => otherCalls++);

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(selfRemovingCalls, Is.EqualTo(1));
            Assert.That(otherCalls, Is.EqualTo(1));
            Assert.That(_eventSystem.GetListenerCount<BroadcastTestEvent>(), Is.EqualTo(1));

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(selfRemovingCalls, Is.EqualTo(1));
            Assert.That(otherCalls, Is.EqualTo(2));
        }

        /// <summary>Removing a listener that has not run yet skips it within the same broadcast.</summary>
        [Test]
        public void ListenerRemovingLaterListener_SkipsItWithinSameBroadcast()
        {
            int middleCalls = 0;
            int lastCalls = 0;
            EventHandle lastHandle = default;

            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) =>
                context.Service.RemoveListener(in lastHandle));
            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) => middleCalls++);
            lastHandle = _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) => lastCalls++);

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(middleCalls, Is.EqualTo(1), "An unrelated listener must not be skipped.");
            Assert.That(lastCalls, Is.Zero, "The removed listener must not run.");
            Assert.That(_eventSystem.GetListenerCount<BroadcastTestEvent>(), Is.EqualTo(2));
        }

        /// <summary>A listener added during a broadcast only runs from the next invocation on.</summary>
        [Test]
        public void ListenerAddedDuringBroadcast_RunsOnNextInvokeOnly()
        {
            int addedListenerCalls = 0;
            bool hasAddedListener = false;

            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) =>
            {
                if (hasAddedListener)
                {
                    return;
                }

                hasAddedListener = true;
                context.Service.AddListener((ref EventContext innerContext, in BroadcastTestEvent innerEvent) =>
                    addedListenerCalls++);
            });

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(addedListenerCalls, Is.Zero);

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(addedListenerCalls, Is.EqualTo(1));
        }

        /// <summary>RemoveAllListeners during a broadcast defers: later listeners are skipped, then all are removed.</summary>
        [Test]
        public void RemoveAllListeners_DuringBroadcast_DefersUntilBroadcastEnds()
        {
            int laterCalls = 0;
            bool removeAllResult = false;

            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) =>
                removeAllResult = context.Service.RemoveAllListeners<BroadcastTestEvent>());
            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) => laterCalls++);

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(removeAllResult, Is.True);
            Assert.That(laterCalls, Is.Zero);
            Assert.That(_eventSystem.GetListenerCount<BroadcastTestEvent>(), Is.Zero);
        }

        /// <summary>Invoking an event from one of its own listeners is a no-op and warns by default.</summary>
        [Test]
        public void RecursiveInvoke_IsNoOp_AndWarnsByDefault()
        {
            ApplySettings(logRecursiveInvocationWarning: true, validateInvocationTargets: true);
            bool innerInvokeResult = true;
            int callCount = 0;

            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) =>
            {
                callCount++;
                innerInvokeResult = context.Service.Invoke(this, new BroadcastTestEvent());
            });

            LogAssert.Expect(LogType.Warning, new Regex("already being invoked"));
            bool outerInvokeResult = _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(outerInvokeResult, Is.True);
            Assert.That(innerInvokeResult, Is.False);
            Assert.That(callCount, Is.EqualTo(1));
        }

        /// <summary>The recursion warning honors a registered EventSettings that disables it.</summary>
        [Test]
        public void RecursiveInvoke_WarningSuppressedByEventSettings()
        {
            ApplySettings(logRecursiveInvocationWarning: false, validateInvocationTargets: true);
            bool innerInvokeResult = true;

            _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) =>
                innerInvokeResult = context.Service.Invoke(this, new BroadcastTestEvent()));

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(innerInvokeResult, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A throwing listener is logged with its method name and never blocks the deferred cleanup.</summary>
        [Test]
        public void ThrowingListener_IsLogged_AndCleanupStillRuns()
        {
            EventHandle throwingHandle = default;

            throwingHandle = _eventSystem.AddListener((ref EventContext context, in BroadcastTestEvent e) =>
            {
                context.Service.RemoveListener(in throwingHandle);
                Throw();
            });

            LogAssert.Expect(LogType.Error, new Regex("An exception occurred while invoking"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(_eventSystem.IsInvoking<BroadcastTestEvent>(), Is.False);
            Assert.That(_eventSystem.GetListenerCount<BroadcastTestEvent>(), Is.Zero,
                "The removal deferred before the throw must still be flushed.");
        }

        /// <summary>A listener whose Unity target was destroyed is skipped and pruned by default.</summary>
        [Test]
        public void DestroyedUnityTarget_IsSkippedAndPruned_ByDefault()
        {
            ApplySettings(logRecursiveInvocationWarning: true, validateInvocationTargets: true);
            var target = ScriptableObject.CreateInstance<UnityTargetListener>();

            _eventSystem.AddListener<BroadcastTestEvent>(target.Handle);
            Object.DestroyImmediate(target);

            bool invoked = _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(invoked, Is.True);
            Assert.That(target.CallCount, Is.Zero);
            Assert.That(_eventSystem.GetListenerCount<BroadcastTestEvent>(), Is.Zero);
        }

        /// <summary>With validation disabled via EventSettings, the destroyed target is still invoked.</summary>
        [Test]
        public void DestroyedUnityTarget_StillInvoked_WhenValidationDisabled()
        {
            ApplySettings(logRecursiveInvocationWarning: true, validateInvocationTargets: false);
            var target = ScriptableObject.CreateInstance<UnityTargetListener>();

            _eventSystem.AddListener<BroadcastTestEvent>(target.Handle);
            Object.DestroyImmediate(target);

            _eventSystem.Invoke(this, new BroadcastTestEvent());

            Assert.That(target.CallCount, Is.EqualTo(1));
            Assert.That(_eventSystem.GetListenerCount<BroadcastTestEvent>(), Is.EqualTo(1));
        }

        // The registry is a soft singleton, so when the project already has an EventSettings registered (e.g. a
        // preloaded asset) a test-created instance would be ignored. Mutate the registered one and restore it on
        // teardown; only create a temporary instance (self-registering through OnEnable) when none exists.
        private void ApplySettings(bool logRecursiveInvocationWarning, bool validateInvocationTargets)
        {
            if (ScriptableSettingsRegistry.TryGet(out EventSettings registered))
            {
                _mutatedSettings = registered;
                _originalLogRecursiveInvocationWarning = registered.LogRecursiveInvocationWarning;
                _originalValidateInvocationTargets = registered.ValidateInvocationTargets;
                SetSettingsFields(registered, logRecursiveInvocationWarning, validateInvocationTargets);

                return;
            }

            _createdSettings = ScriptableObject.CreateInstance<EventSettings>();
            SetSettingsFields(_createdSettings, logRecursiveInvocationWarning, validateInvocationTargets);
        }

        // The private serialized fields are set via reflection because tests must not widen the public API.
        private static void SetSettingsFields(EventSettings settings, bool logRecursiveInvocationWarning,
            bool validateInvocationTargets)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

            typeof(EventSettings).GetField("_logRecursiveInvocationWarning", flags)
                .SetValue(settings, logRecursiveInvocationWarning);
            typeof(EventSettings).GetField("_validateInvocationTargets", flags)
                .SetValue(settings, validateInvocationTargets);
        }

        private static void Throw()
        {
            throw new InvalidOperationException("Listener failure for the exception isolation test.");
        }
    }
}
