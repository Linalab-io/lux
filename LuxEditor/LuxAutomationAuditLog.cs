using System;
using System.Collections.Generic;

namespace Linalab.Lux.Editor
{
    public readonly struct LuxAutomationAuditEntry
    {
        public LuxAutomationAuditEntry(
            DateTime timestampUtc,
            string actor,
            string commandKind,
            string command,
            string targetContext,
            bool allowed,
            bool success,
            string result)
        {
            TimestampUtc = timestampUtc;
            Actor = actor ?? string.Empty;
            CommandKind = commandKind ?? string.Empty;
            Command = command ?? string.Empty;
            TargetContext = targetContext ?? string.Empty;
            Allowed = allowed;
            Success = success;
            Result = result ?? string.Empty;
        }

        public DateTime TimestampUtc { get; }
        public string Actor { get; }
        public string CommandKind { get; }
        public string Command { get; }
        public string TargetContext { get; }
        public bool Allowed { get; }
        public bool Success { get; }
        public string Result { get; }
    }

    public sealed class LuxAutomationAuditLog
    {
        const int DefaultCapacity = 200;

        readonly int _capacity;
        readonly Queue<LuxAutomationAuditEntry> _entries;

        public LuxAutomationAuditLog(int capacity = DefaultCapacity)
        {
            _capacity = Math.Max(1, capacity);
            _entries = new Queue<LuxAutomationAuditEntry>(_capacity);
        }

        public IReadOnlyCollection<LuxAutomationAuditEntry> Entries => _entries.ToArray();

        public LuxAutomationAuditEntry Record(
            string actor,
            string commandKind,
            string command,
            string targetContext,
            bool allowed,
            bool success,
            string result)
        {
            var entry = new LuxAutomationAuditEntry(
                DateTime.UtcNow,
                actor,
                commandKind,
                command,
                targetContext,
                allowed,
                success,
                result);

            while (_entries.Count >= _capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
            return entry;
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
