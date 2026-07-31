using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Fy.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Fy.EventSystem.RuntimeTests
{
    /// <summary>
    /// Play-mode stress simulation of a horde survival game: thousands of enemy MonoBehaviours subscribing on spawn,
    /// a tick plus a burst of damage events every frame, enemies dying (and unsubscribing) mid-broadcast, careless
    /// enemies destroyed without unsubscribing, listeners that throw, and listeners that re-invoke their own event.
    /// </summary>
    /// <remarks>
    /// The point is not a pass/fail micro-assertion but the combination: correctness invariants that must hold under
    /// load (no leaked listeners, no lost broadcasts, no escaping exceptions) plus a timing report logged at the end.
    /// Numbers are machine dependent — compare runs against each other, not against a fixed budget.
    /// </remarks>
    public sealed class EventSystemStressTests
    {
        private const int InitialHordeSize = 2000;
        private const int CombatFrames = 120;
        private const int DamageEventsPerFrame = 20;
        private const int SpawnsPerFrame = 5;
        private const int ChaosEveryFrames = 30;
        private const int WaveWipeSize = 5000;
        private const int ThroughputListeners = 100;
        private const int ThroughputInvocations = 50_000;
        private const int SweepTotalDeliveries = 2_000_000;
        private const long AllocationBudgetBytes = 64 * 1024;

        private readonly struct GameTickEvent : IEvent
        {
            internal readonly int Frame;

            internal GameTickEvent(int frame)
            {
                Frame = frame;
            }
        }

        private readonly struct DamageEvent : IEvent
        {
            internal readonly int TargetId;
            internal readonly int Amount;

            internal DamageEvent(int targetId, int amount)
            {
                TargetId = targetId;
                Amount = amount;
            }
        }

        private readonly struct EnemyDiedEvent : IEvent
        {
            internal readonly int Id;

            internal EnemyDiedEvent(int id)
            {
                Id = id;
            }
        }

        private readonly struct WaveClearedEvent : IEvent { }

        /// <summary>
        /// A game object that behaves like real gameplay code: subscribes when it enters play, reacts to every
        /// broadcast, and unsubscribes the moment it dies. A <see cref="Leaky"/> one skips the unsubscribe, which is
        /// the careless-code case the invocation-target validation exists to survive.
        /// </summary>
        private sealed class StressEnemy : MonoBehaviour
        {
            internal static int TickDeliveries;
            internal static int DamageDeliveries;
            internal static int Deaths;

            private IEventService _service;
            private EventHandle _tickHandle;
            private EventHandle _damageHandle;
            private int _id;
            private int _health;

            internal bool Leaky { get; private set; }

            internal void Init(IEventService service, int id, bool leaky)
            {
                _service = service;
                _id = id;
                _health = 3;
                Leaky = leaky;

                _tickHandle = service.AddListener<GameTickEvent>(HandleTick);
                _damageHandle = service.AddListener<DamageEvent>(HandleDamage);
            }

            private void OnDisable()
            {
                if (Leaky)
                {
                    return;
                }

                _tickHandle.RemoveListener();
                _damageHandle.RemoveListener();
            }

            private void HandleTick(ref EventContext context, in GameTickEvent e)
            {
                TickDeliveries++;
            }

            private void HandleDamage(ref EventContext context, in DamageEvent e)
            {
                DamageDeliveries++;

                if (e.TargetId != _id)
                {
                    return;
                }

                _health -= e.Amount;

                if (_health > 0)
                {
                    return;
                }

                Deaths++;

                context.Service.Invoke(this, new EnemyDiedEvent(_id));
                _tickHandle.RemoveListener();
                _damageHandle.RemoveListener();
                Destroy(gameObject);
            }
        }

        private readonly List<StressEnemy> _horde = new(InitialHordeSize);

        private IEventService _service;
        private System.Random _random;
        private int _nextEnemyId;
        private int _deathNotifications;
        private int _abortedBroadcasts;
        private int _recursionEscapes;
        private EventHandle _deathCounterHandle;

        [SetUp]
        public void SetUp()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Stress simulation needs real frames and MonoBehaviour lifecycle; run it in Play Mode.");
            }

            _service = ServiceLocator.GetChecked<IEventService>();
            _random = new System.Random(20260730);
            _nextEnemyId = 0;
            _deathNotifications = 0;
            _abortedBroadcasts = 0;
            _recursionEscapes = 0;
            StressEnemy.TickDeliveries = 0;
            StressEnemy.DamageDeliveries = 0;
            StressEnemy.Deaths = 0;

            _deathCounterHandle = _service.AddListener((ref EventContext context, in EnemyDiedEvent e) =>
            {
                _deathNotifications++;
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (StressEnemy enemy in _horde)
            {
                if (enemy != null)
                {
                    Object.DestroyImmediate(enemy.gameObject);
                }
            }

            _horde.Clear();

            _deathCounterHandle.RemoveListener();
            _service.RemoveAllListeners<GameTickEvent>();
            _service.RemoveAllListeners<DamageEvent>();
            _service.RemoveAllListeners<EnemyDiedEvent>();
            _service.RemoveAllListeners<WaveClearedEvent>();
        }

        /// <summary>
        /// Runs the full simulation and asserts the invariants that must survive it: every listener of a dead or
        /// destroyed enemy is gone, every death was announced, and no listener re-entered its own event.
        /// </summary>
        [UnityTest]
        public IEnumerator HordeSurvival_UnderCombatLoad_LeavesNoLeakedListeners()
        {
            for (int i = 0; i < InitialHordeSize; i++)
            {
                Spawn(leaky: false);
            }

            double[] frameMilliseconds = new double[CombatFrames];
            Stopwatch stopwatch = new();

            for (int frame = 0; frame < CombatFrames; frame++)
            {
                stopwatch.Restart();

                _service.Invoke(this, new GameTickEvent(frame));

                for (int i = 0; i < DamageEventsPerFrame; i++)
                {
                    _service.Invoke(this, new DamageEvent(_random.Next(_nextEnemyId), 3));
                }

                stopwatch.Stop();
                frameMilliseconds[frame] = stopwatch.Elapsed.TotalMilliseconds;

                if (frame % ChaosEveryFrames == ChaosEveryFrames - 1)
                {
                    RunChaosBurst(frame);
                }

                for (int i = 0; i < SpawnsPerFrame; i++)
                {
                    Spawn(leaky: false);
                }

                yield return null;
            }

            yield return null;

            _service.Invoke(this, new GameTickEvent(CombatFrames));
            _service.Invoke(this, new DamageEvent(-1, 0));

            int aliveEnemies = 0;

            foreach (StressEnemy enemy in _horde)
            {
                if (enemy != null)
                {
                    aliveEnemies++;
                }
            }

            long deliveries = StressEnemy.TickDeliveries + StressEnemy.DamageDeliveries;

            Report("Combat frame (tick + " + DamageEventsPerFrame + " damage events)", frameMilliseconds);
            Debug.Log($"[Stress] Spawned {_nextEnemyId}, alive {aliveEnemies}, deaths {StressEnemy.Deaths}, " +
                      $"{deliveries:N0} listener calls, " +
                      $"{Total(frameMilliseconds) * 1_000_000d / Math.Max(1L, deliveries):F0} ns per call. " +
                      $"Broadcasts aborted by a throwing listener: {_abortedBroadcasts}.");

            Assert.That(_service.GetListenerCount<GameTickEvent>(), Is.EqualTo(aliveEnemies),
                "Dead and destroyed enemies must leave no tick listener behind.");
            Assert.That(_service.GetListenerCount<DamageEvent>(), Is.EqualTo(aliveEnemies),
                "Dead and destroyed enemies must leave no damage listener behind.");
            Assert.That(StressEnemy.Deaths, Is.GreaterThan(0), "The simulation never killed anything; check the seed.");
            Assert.That(_deathNotifications, Is.EqualTo(StressEnemy.Deaths),
                "Every nested EnemyDiedEvent fired from inside a DamageEvent broadcast must have been delivered.");
            Assert.That(_recursionEscapes, Is.Zero, "A listener re-entered its own event type.");
        }

        /// <summary>
        /// The wave-wipe case: every listener of an event unsubscribes itself from inside the broadcast that tells
        /// it the wave is over. Correctness is asserted, cost is reported.
        /// </summary>
        [Test]
        public void WaveWipe_EveryListenerUnsubscribesMidBroadcast_ClearsTheEvent()
        {
            for (int i = 0; i < WaveWipeSize; i++)
            {
                _service.AddListener((ref EventContext context, in WaveClearedEvent e) =>
                {
                    context.Service.RemoveListener(in context.CurrentHandle);
                });
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            bool invoked = _service.Invoke(this, new WaveClearedEvent());
            stopwatch.Stop();

            Assert.That(invoked, Is.True);
            Assert.That(_service.GetListenerCount<WaveClearedEvent>(), Is.Zero,
                "Every deferred removal must be flushed when the broadcast ends.");

            Debug.Log($"[Stress] {WaveWipeSize:N0} self-unsubscribes inside one broadcast: " +
                      $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms.");
        }

        /// <summary>
        /// Steady-state broadcasting: the hot path a shipped game runs thousands of times per frame must not feed
        /// the garbage collector.
        /// </summary>
        [Test]
        public void SteadyStateBroadcast_DoesNotAllocate()
        {
            int deliveries = 0;

            for (int i = 0; i < ThroughputListeners; i++)
            {
                _service.AddListener((ref EventContext context, in DamageEvent e) =>
                {
                    deliveries++;
                });
            }

            for (int i = 0; i < 1000; i++)
            {
                _service.Invoke(this, new DamageEvent(i, 1));
            }

            long before = GC.GetTotalMemory(true);
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < ThroughputInvocations; i++)
            {
                _service.Invoke(this, new DamageEvent(i, 1));
            }

            stopwatch.Stop();
            long allocated = GC.GetTotalMemory(false) - before;
            long calls = (long)ThroughputInvocations * ThroughputListeners;

            Debug.Log($"[Stress] {ThroughputInvocations:N0} broadcasts x {ThroughputListeners} listeners = " +
                      $"{calls:N0} calls in {stopwatch.Elapsed.TotalMilliseconds:F1} ms " +
                      $"({stopwatch.Elapsed.TotalMilliseconds * 1_000_000d / calls:F0} ns per call), " +
                      $"{allocated / 1024d:F1} KB allocated.");

            Assert.That(deliveries, Is.EqualTo(calls + 1000L * ThroughputListeners));
            Assert.That(allocated, Is.LessThan(AllocationBudgetBytes),
                $"Broadcasting allocated {allocated} bytes; the hot path must stay allocation free.");
        }

        /// <summary>
        /// Sweeps the listener count so the fixed cost of a broadcast can be told apart from the cost of a single
        /// delivery — the two are indistinguishable from any single measurement.
        /// </summary>
        /// <remarks>
        /// Time per broadcast is <c>F + D * n</c>: F is the per-broadcast work (the event lookup and the settings
        /// lookup), D is the per-delivery work. Two rows are enough to solve it:
        /// <c>D = (t256 - t1) / 255</c> and <c>F = t1 - D</c>. The rows in between confirm it is actually linear —
        /// a bend upwards at the high end is cache pressure, which is a finding rather than noise.
        /// Total deliveries are held constant so every case does the same amount of listener work.
        /// </remarks>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(16)]
        [TestCase(64)]
        [TestCase(256)]
        public void BroadcastCost_ScalesWithListenerCount(int listenerCount)
        {
            int broadcasts = SweepTotalDeliveries / listenerCount;

            for (int i = 0; i < listenerCount; i++)
            {
                _service.AddListener((ref EventContext context, in DamageEvent e) => { });
            }

            for (int i = 0; i < 1000; i++)
            {
                _service.Invoke(this, new DamageEvent(i, 1));
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < broadcasts; i++)
            {
                _service.Invoke(this, new DamageEvent(i, 1));
            }

            stopwatch.Stop();

            double nanosecondsPerBroadcast = stopwatch.Elapsed.TotalMilliseconds * 1_000_000d / broadcasts;

            Debug.Log($"[Sweep] n={listenerCount,3}: {nanosecondsPerBroadcast,8:F1} ns per broadcast, " +
                      $"{nanosecondsPerBroadcast / listenerCount,7:F1} ns per delivery " +
                      $"({broadcasts:N0} broadcasts).");
        }

        private StressEnemy Spawn(bool leaky)
        {
            GameObject gameObject = new(leaky ? "LeakyEnemy" : "Enemy");
            StressEnemy enemy = gameObject.AddComponent<StressEnemy>();

            enemy.Init(_service, _nextEnemyId++, leaky);
            _horde.Add(enemy);

            return enemy;
        }

        /// <summary>
        /// The three ways gameplay code abuses an event bus, fired periodically into the running simulation: a
        /// listener that throws, a listener that re-invokes its own event, and an object destroyed without ever
        /// unsubscribing.
        /// </summary>
        private void RunChaosBurst(int frame)
        {
            bool sentinelReached = false;

            EventHandle thrower = _service.AddListener((ref EventContext context, in DamageEvent e) =>
                throw new InvalidOperationException("Chaos listener throwing on purpose."));
            EventHandle sentinel = _service.AddListener((ref EventContext context, in DamageEvent e) =>
            {
                sentinelReached = true;
            });

            LogAssert.Expect(LogType.Error, new Regex("An exception occurred while invoking"));
            LogAssert.Expect(LogType.Exception, new Regex("Chaos listener throwing on purpose"));

            _service.Invoke(this, new DamageEvent(-1, 0));

            if (!sentinelReached)
            {
                _abortedBroadcasts++;
            }

            thrower.RemoveListener();
            sentinel.RemoveListener();

            EventHandle recursive = _service.AddListener((ref EventContext context, in GameTickEvent e) =>
            {
                if (context.Service.Invoke(this, new GameTickEvent(-1)))
                {
                    _recursionEscapes++;
                }
            });

            _service.Invoke(this, new GameTickEvent(frame));
            recursive.RemoveListener();

            Object.Destroy(Spawn(leaky: true).gameObject);
        }

        private static void Report(string label, double[] samples)
        {
            double[] sorted = (double[])samples.Clone();
            Array.Sort(sorted);

            Debug.Log($"[Stress] {label} over {samples.Length} frames: " +
                      $"median {sorted[sorted.Length / 2]:F3} ms, " +
                      $"p99 {sorted[(int)(sorted.Length * 0.99d)]:F3} ms, " +
                      $"max {sorted[sorted.Length - 1]:F3} ms, " +
                      $"total {Total(samples):F1} ms.");
        }

        private static double Total(double[] samples)
        {
            double total = 0d;

            foreach (double sample in samples)
            {
                total += sample;
            }

            return total;
        }
    }
}
