using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Linalab.LuxEditor.Tests
{
    public sealed class LuxAiActionLogTests
    {
        string _tempDirectory;
        readonly List<UnityEditor.LuxAiActionLog> _configuredLogs = new List<UnityEditor.LuxAiActionLog>();

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "LuxAiActionLogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEditor.LuxAiActionLogBroadcaster.ConfigureForTests(null, null, null);
            foreach (var configuredLog in _configuredLogs)
            {
                configuredLog.Dispose();
            }

            _configuredLogs.Clear();

            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Test]
        public void Record_AppendsValidJsonlLine()
        {
            string path = GetTempLogPath();
            using (var log = new UnityEditor.LuxAiActionLog(logPath: path))
            {
                log.Record("opencode", "agent", "compile", "run", "Editor", "Compilation requested");
                log.Flush();
            }

            string[] lines = File.ReadAllLines(path);

            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(lines[0], Does.StartWith("{"));
            Assert.That(lines[0], Does.EndWith("}"));
            Assert.That(lines[0], Does.Contain("\"schemaVersion\":1"));
            Assert.That(lines[0], Does.Contain("\"protocol\":\"lux.ai.action_log.v1\""));
            Assert.That(lines[0], Does.Contain("\"action\":\"run\""));
        }

        [Test]
        public void Recent_RespectsCapacity()
        {
            string path = GetTempLogPath();
            using var log = new UnityEditor.LuxAiActionLog(recentCapacity: 2, logPath: path);

            log.Record("test", "agent", "one", "first", "target", "first message");
            log.Record("test", "agent", "two", "second", "target", "second message");
            log.Record("test", "agent", "three", "third", "target", "third message");

            var recent = log.Recent();

            Assert.That(recent, Has.Count.EqualTo(2));
            Assert.That(recent[0].action, Is.EqualTo("second"));
            Assert.That(recent[1].action, Is.EqualTo("third"));
        }

        [Test]
        public void Record_RedactsSecretsBeforePersistenceAndRecentWindow()
        {
            string path = GetTempLogPath();
            using var log = new UnityEditor.LuxAiActionLog(logPath: path);

            var entry = log.Record(
                "test",
                "agent",
                "remote",
                "call",
                "Authorization: Bearer secret-token",
                "Bearer secret-token password=secret-token",
                metadata: new Dictionary<string, string>
                {
                    ["apiKey"] = "secret-token",
                    ["note"] = "Authorization: Bearer secret-token"
                });
            log.Flush();

            string jsonl = File.ReadAllText(path);

            Assert.That(entry.message, Does.Not.Contain("secret-token"));
            Assert.That(jsonl, Does.Not.Contain("secret-token"));
            Assert.That(jsonl.ToLowerInvariant(), Does.Not.Contain("bearer secret-token"));
            Assert.That(jsonl, Does.Contain("[REDACTED]"));
        }

        [Test]
        public void Compact_KeepsValidJsonlAndReportsCompactedState()
        {
            string path = GetTempLogPath();
            using var log = new UnityEditor.LuxAiActionLog(logPath: path);

            log.Record("test", "agent", "first", "one", "target", "message one");
            log.Record("test", "agent", "second", "two", "target", "message two");
            log.Flush();
            File.AppendAllText(path, "not-json\n");

            var result = log.Compact(maxLines: 1);
            string[] lines = File.ReadAllLines(path);

            Assert.That(result.OriginalLineCount, Is.EqualTo(3));
            Assert.That(result.ValidLineCount, Is.EqualTo(2));
            Assert.That(result.WrittenLineCount, Is.EqualTo(1));
            Assert.That(result.Compacted, Is.True);
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(lines[0], Does.Contain("\"action\":\"two\""));
            Assert.That(lines[0], Does.Not.Contain("not-json"));
        }

        [Test]
        public void Context_SortsEntriesAndGroupsCounts()
        {
            string path = GetTempLogPath();
            using var log = new UnityEditor.LuxAiActionLog(logPath: path);

            log.Record("test", "alpha", "compile", "build", "target", "message");
            log.Record("test", "alpha", "compile", "test", "target", "message");
            log.Record("test", "beta", "git", "status", "target", "message");

            var context = log.Context();

            Assert.That(context.Entries, Has.Count.EqualTo(3));
            Assert.That(context.Entries[0].action, Is.EqualTo("build"));
            Assert.That(context.Entries[1].action, Is.EqualTo("test"));
            Assert.That(context.Entries[2].action, Is.EqualTo("status"));
            Assert.That(context.CategoryCounts["compile"], Is.EqualTo(2));
            Assert.That(context.CategoryCounts["git"], Is.EqualTo(1));
            Assert.That(context.ActorCounts["alpha"], Is.EqualTo(2));
            Assert.That(context.ActorCounts["beta"], Is.EqualTo(1));
        }

        [Test]
        public void Broadcaster_QueuesBroadcastAndPumpsInBatches()
        {
            var broadcasts = new List<Tuple<string, UnityEditor.LuxAiActionLogEntry>>();
            ConfigureBroadcasterForTest(0.0, (eventType, payload) => broadcasts.Add(Tuple.Create(eventType, (UnityEditor.LuxAiActionLogEntry)payload)));

            for (int index = 0; index < 20; index++)
            {
                UnityEditor.LuxAiActionLogBroadcaster.Record("test", "action-" + index, "target", "message");
            }

            Assert.That(broadcasts, Is.Empty);

            int firstPumpCount = UnityEditor.LuxAiActionLogBroadcaster.PumpForTests();
            int secondPumpCount = UnityEditor.LuxAiActionLogBroadcaster.PumpForTests();

            Assert.That(firstPumpCount, Is.EqualTo(16));
            Assert.That(secondPumpCount, Is.EqualTo(4));
            Assert.That(broadcasts, Has.Count.EqualTo(20));
            Assert.That(broadcasts[0].Item1, Is.EqualTo("ai_action_log"));
            Assert.That(broadcasts[0].Item2.actor, Is.EqualTo("user"));
        }

        [Test]
        public void Broadcaster_PropagatesAmbientAttributionUntilTtlExpires()
        {
            double now = 10.0;
            ConfigureBroadcasterForTest(now, null, () => now);

            using (UnityEditor.LuxAiActionLogBroadcaster.PushAttribution("opencode", "dynamic-code", "corr-1"))
            {
                UnityEditor.LuxAiActionLogBroadcaster.Record("test", "inside", "target", "message");
            }

            now += 1.0;
            var propagated = UnityEditor.LuxAiActionLogBroadcaster.Record("test", "propagated", "target", "message");
            now += 2.1;
            var expired = UnityEditor.LuxAiActionLogBroadcaster.Record("test", "expired", "target", "message");

            Assert.That(propagated.actor, Is.EqualTo("opencode"));
            Assert.That(propagated.source, Is.EqualTo("dynamic-code"));
            Assert.That(propagated.metadata["correlationId"], Is.EqualTo("corr-1"));
            Assert.That(expired.actor, Is.EqualTo("user"));
            Assert.That(expired.source, Is.EqualTo("unity-editor"));
            Assert.That(expired.metadata.ContainsKey("correlationId"), Is.False);
        }

        [Test]
        public void RecordDynamicCodeExecution_StoresLengthAndHashWithoutRawCode()
        {
            string path = GetTempLogPath();
            using var log = new UnityEditor.LuxAiActionLog(logPath: path);
            UnityEditor.LuxAiActionLogBroadcaster.ConfigureForTests(log, null, () => 0.0);
            string code = "Debug.Log(\"raw dynamic content\");";

            var entry = UnityEditor.LuxAiActionLogBroadcaster.RecordDynamicCodeExecution(code, true, "completed", 0);
            log.Flush();

            string jsonl = File.ReadAllText(path);
            Assert.That(entry.metadata["codeLength"], Is.EqualTo(code.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(entry.metadata["codeHash"], Has.Length.EqualTo(64));
            Assert.That(jsonl, Does.Not.Contain("raw dynamic content"));
            Assert.That(jsonl, Does.Not.Contain("Debug.Log"));
        }

        [Test]
        public void ExplicitSceneCloseRecordMethod_CapturesUnsafeHookReplacement()
        {
            ConfigureBroadcasterForTest(0.0);

            var entry = UnityEditor.LuxAiActionLogBroadcaster.RecordSceneClosedUnsafeExplicit("Assets/Test.unity");

            Assert.That(entry.category, Is.EqualTo("scene"));
            Assert.That(entry.action, Is.EqualTo("close_explicit"));
            Assert.That(entry.target, Is.EqualTo("Assets/Test.unity"));
            Assert.That(entry.message, Does.Contain("explicit record method"));
        }

        string GetTempLogPath()
        {
            return Path.Combine(_tempDirectory, ".lux", "ai-action-log.jsonl");
        }

        void ConfigureBroadcasterForTest(double now, Action<string, object> broadcastSink = null, Func<double> timeProvider = null)
        {
            var testLog = new UnityEditor.LuxAiActionLog(logPath: GetTempLogPath());
            _configuredLogs.Add(testLog);
            UnityEditor.LuxAiActionLogBroadcaster.ConfigureForTests(testLog, broadcastSink, timeProvider ?? (() => now));
        }
    }
}
