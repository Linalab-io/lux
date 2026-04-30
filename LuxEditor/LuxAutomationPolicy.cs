using System;
using System.Collections.Generic;
using System.Linq;

namespace Linalab.Lux.Editor
{
    public enum LuxAutomationDecisionKind
    {
        Allow,
        RequireApproval,
        Block
    }

    public readonly struct LuxAutomationDecision
    {
        public LuxAutomationDecision(LuxAutomationDecisionKind kind, string reason)
        {
            Kind = kind;
            Reason = reason ?? string.Empty;
        }

        public LuxAutomationDecisionKind Kind { get; }
        public string Reason { get; }
        public bool CanExecuteAutomatically => Kind == LuxAutomationDecisionKind.Allow;
    }

    public sealed class LuxAutomationPolicy
    {
        static readonly string[] DefaultBlockedTokens =
        {
            "rm -rf",
            "sudo",
            "mkfs",
            "dd if=",
            ":(){",
            "chmod -R 777",
            "chown -R",
            "git reset --hard",
            "git clean -fd",
            "git clean -xfd",
            "git push --force",
            "git push -f",
            "git branch -D",
            "git checkout -- ."
        };

        static readonly string[] DefaultApprovalTokens =
        {
            "git push",
            "git checkout",
            "git switch",
            "git merge",
            "git rebase",
            "git submodule update",
            "npm install",
            "pnpm install",
            "yarn install",
            "brew install"
        };

        readonly List<string> _blockedTokens;
        readonly List<string> _approvalTokens;

        public LuxAutomationPolicy(IEnumerable<string> blockedTokens = null, IEnumerable<string> approvalTokens = null)
        {
            _blockedTokens = NormalizeTokens(blockedTokens ?? DefaultBlockedTokens);
            _approvalTokens = NormalizeTokens(approvalTokens ?? DefaultApprovalTokens);
        }

        public IReadOnlyList<string> BlockedTokens => _blockedTokens;
        public IReadOnlyList<string> ApprovalTokens => _approvalTokens;

        public LuxAutomationDecision Evaluate(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return new LuxAutomationDecision(LuxAutomationDecisionKind.Block, "Command is empty.");
            }

            var normalizedCommand = Normalize(command);
            var blockedToken = _blockedTokens.FirstOrDefault(token => normalizedCommand.Contains(token));
            if (!string.IsNullOrEmpty(blockedToken))
            {
                return new LuxAutomationDecision(LuxAutomationDecisionKind.Block, $"Command contains blocked token: {blockedToken}");
            }

            var approvalToken = _approvalTokens.FirstOrDefault(token => normalizedCommand.Contains(token));
            if (!string.IsNullOrEmpty(approvalToken))
            {
                return new LuxAutomationDecision(LuxAutomationDecisionKind.RequireApproval, $"Command requires approval: {approvalToken}");
            }

            return new LuxAutomationDecision(LuxAutomationDecisionKind.Allow, "Command allowed by broad automation policy.");
        }

        static List<string> NormalizeTokens(IEnumerable<string> tokens)
        {
            return tokens
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(Normalize)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        static string Normalize(string value)
        {
            return value.Trim().ToLowerInvariant();
        }
    }
}
