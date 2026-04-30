using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Linalab.Lux.Editor
{
    public readonly struct LuxCodeExecutionPolicyDecision
    {
        public LuxCodeExecutionPolicyDecision(bool allowed, string blockedToken, string errorCode, string message)
        {
            Allowed = allowed;
            BlockedToken = blockedToken ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Allowed { get; }
        public string BlockedToken { get; }
        public string ErrorCode { get; }
        public string Message { get; }
    }

    public sealed class LuxCodeExecutionPolicy
    {
        public const string ErrorCodeBlocked = "dynamic_code_policy_blocked";
        public const string AllowedOutputRoot = "Temp/Lux/";

        static readonly ForbiddenPattern[] DefaultForbiddenPatterns =
        {
            new ForbiddenPattern("System.IO", @"\b(?:global::)?System\s*\.\s*IO\b"),
            new ForbiddenPattern("File.", @"(?<![A-Za-z0-9_])File\s*\."),
            new ForbiddenPattern("Directory.", @"(?<![A-Za-z0-9_])Directory\s*\."),
            new ForbiddenPattern("FileStream", @"\bFileStream\b"),
            new ForbiddenPattern("StreamWriter", @"\bStreamWriter\b"),
            new ForbiddenPattern("AssetDatabase.CreateFolder", @"\bAssetDatabase\s*\.\s*CreateFolder\b"),
            new ForbiddenPattern(".cs", @"\.cs(?=$|[^A-Za-z0-9_])", RegexOptions.IgnoreCase),
            new ForbiddenPattern(".asmdef", @"\.asmdef(?=$|[^A-Za-z0-9_])", RegexOptions.IgnoreCase)
        };

        readonly List<ForbiddenPattern> _forbiddenPatterns;

        public LuxCodeExecutionPolicy()
            : this(DefaultForbiddenPatterns)
        {
        }

        LuxCodeExecutionPolicy(IEnumerable<ForbiddenPattern> forbiddenPatterns)
        {
            _forbiddenPatterns = forbiddenPatterns
                .Where(pattern => pattern.IsValid)
                .ToList();
        }

        public IReadOnlyList<string> ForbiddenTokens => _forbiddenPatterns.Select(pattern => pattern.Token).ToArray();

        public (bool allowed, string blockedToken) IsCodeAllowed(string code)
        {
            var decision = Evaluate(code);
            return (decision.Allowed, decision.BlockedToken);
        }

        public LuxCodeExecutionPolicyDecision Evaluate(string code)
        {
            var blockedToken = FindBlockedToken(code ?? string.Empty);
            if (string.IsNullOrEmpty(blockedToken))
            {
                return new LuxCodeExecutionPolicyDecision(
                    true,
                    string.Empty,
                    string.Empty,
                    "Dynamic code allowed by Lux policy.");
            }

            return new LuxCodeExecutionPolicyDecision(
                false,
                blockedToken,
                ErrorCodeBlocked,
                CreateBlockedMessage(blockedToken));
        }

        public bool IsOutputPathAllowed(string projectRelativePath)
        {
            if (string.IsNullOrWhiteSpace(projectRelativePath))
            {
                return false;
            }

            var normalizedPath = NormalizePath(projectRelativePath);
            if (normalizedPath.StartsWith("/", StringComparison.Ordinal) || ContainsParentTraversal(normalizedPath))
            {
                return false;
            }

            return normalizedPath.StartsWith(AllowedOutputRoot, StringComparison.Ordinal);
        }

        public static string CreateBlockedMessage(string blockedToken)
        {
            var token = string.IsNullOrEmpty(blockedToken) ? "unknown" : blockedToken;
            return $"Dynamic code rejected by Lux policy. Blocked token: {token}";
        }

        string FindBlockedToken(string code)
        {
            foreach (var pattern in _forbiddenPatterns)
            {
                if (pattern.Matches(code))
                {
                    return pattern.Token;
                }
            }

            return string.Empty;
        }

        static string NormalizePath(string path)
        {
            return path.Trim().Replace('\\', '/');
        }

        static bool ContainsParentTraversal(string normalizedPath)
        {
            return normalizedPath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
        }

        readonly struct ForbiddenPattern
        {
            readonly Regex _regex;

            public ForbiddenPattern(string token, string pattern, RegexOptions options = RegexOptions.None)
            {
                Token = token ?? string.Empty;
                _regex = string.IsNullOrWhiteSpace(pattern)
                    ? null
                    : new Regex(pattern, options | RegexOptions.CultureInvariant);
            }

            public string Token { get; }
            public bool IsValid => !string.IsNullOrEmpty(Token) && _regex != null;

            public bool Matches(string code)
            {
                return _regex != null && _regex.IsMatch(code ?? string.Empty);
            }
        }
    }
}
