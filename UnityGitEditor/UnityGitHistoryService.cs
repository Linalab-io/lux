using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Linalab.UnityGit.Editor
{
    public readonly struct GitCommitEntry
    {
        public GitCommitEntry(string hash, string shortHash, string authorName, DateTimeOffset authoredAt, string subject, string[] parentHashes)
        {
            Hash = hash ?? string.Empty;
            ShortHash = shortHash ?? string.Empty;
            AuthorName = authorName ?? string.Empty;
            AuthoredAt = authoredAt;
            Subject = subject ?? string.Empty;
            ParentHashes = parentHashes ?? Array.Empty<string>();
        }

        public string Hash { get; }
        public string ShortHash { get; }
        public string AuthorName { get; }
        public DateTimeOffset AuthoredAt { get; }
        public string Subject { get; }
        public string[] ParentHashes { get; }
    }

    public sealed class GitHistorySnapshot
    {
        public GitHistorySnapshot(bool isRepository, string repositoryRoot, string errorMessage, string friendlyMessage, int maxCount, IReadOnlyList<GitCommitEntry> entries)
        {
            IsRepository = isRepository;
            RepositoryRoot = repositoryRoot ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            FriendlyMessage = friendlyMessage ?? string.Empty;
            MaxCount = maxCount;
            Entries = entries ?? Array.Empty<GitCommitEntry>();
        }

        public bool IsRepository { get; }
        public string RepositoryRoot { get; }
        public string ErrorMessage { get; }
        public string FriendlyMessage { get; }
        public int MaxCount { get; }
        public IReadOnlyList<GitCommitEntry> Entries { get; }
    }

    public static class UnityGitHistoryService
    {
        const int DefaultMaxCount = 50;
        const char FieldSeparator = '\u001f';

        static readonly string[] ResolveRepoRootArguments = { "rev-parse", "--show-toplevel" };

        public static GitHistorySnapshot ReadHistory(string projectRootOverride = null, int maxCount = DefaultMaxCount)
        {
            var normalizedMaxCount = NormalizeMaxCount(maxCount);
            var projectRoot = ResolveProjectRoot(projectRootOverride);

            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return new GitHistorySnapshot(false, string.Empty, "Unity project root could not be resolved.", string.Empty, normalizedMaxCount, Array.Empty<GitCommitEntry>());
            }

            var repoRootResult = GitCommandRunner.Execute(projectRoot, ResolveRepoRootArguments);
            if (!repoRootResult.Success)
            {
                return new GitHistorySnapshot(false, string.Empty, NormalizeGitErrorMessage(repoRootResult.ErrorMessage), NormalizeGitErrorMessage(repoRootResult.ErrorMessage), normalizedMaxCount, Array.Empty<GitCommitEntry>());
            }

            var repoRoot = repoRootResult.Output.Trim();
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            {
                return new GitHistorySnapshot(false, string.Empty, "Git repository root could not be resolved.", string.Empty, normalizedMaxCount, Array.Empty<GitCommitEntry>());
            }

            var historyResult = GitCommandRunner.Execute(repoRoot, BuildHistoryArguments(normalizedMaxCount));
            if (historyResult.Success)
            {
                return ParseHistoryOutput(historyResult.Output, repoRoot, normalizedMaxCount);
            }

            if (IsEmptyRepositoryError(historyResult))
            {
                return new GitHistorySnapshot(true, repoRoot, string.Empty, "This repository has no commits yet.", normalizedMaxCount, Array.Empty<GitCommitEntry>());
            }

            var errorMessage = NormalizeGitErrorMessage(historyResult.ErrorMessage);
            return new GitHistorySnapshot(false, repoRoot, errorMessage, errorMessage, normalizedMaxCount, Array.Empty<GitCommitEntry>());
        }

        public static GitHistorySnapshot ParseHistoryOutput(string output, string repositoryRoot, int maxCount)
        {
            var normalizedOutput = output ?? string.Empty;
            var lines = normalizedOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var entries = new List<GitCommitEntry>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryParseHistoryLine(line, out var fields, out var subject, out var parentHashes))
                {
                    continue;
                }

                if (fields.Length < 5)
                {
                    continue;
                }

                if (!DateTimeOffset.TryParse(fields[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var authoredAt))
                {
                    authoredAt = default;
                }

                entries.Add(new GitCommitEntry(fields[0], fields[1], fields[2], authoredAt, subject, parentHashes));
            }

            return new GitHistorySnapshot(true, repositoryRoot ?? string.Empty, string.Empty, string.Empty, maxCount, entries);
        }

        static bool TryParseHistoryLine(string line, out string[] fields, out string subject, out string[] parentHashes)
        {
            fields = Array.Empty<string>();
            subject = string.Empty;
            parentHashes = Array.Empty<string>();

            var firstSeparator = line.IndexOf(FieldSeparator);
            var secondSeparator = firstSeparator < 0 ? -1 : line.IndexOf(FieldSeparator, firstSeparator + 1);
            var thirdSeparator = secondSeparator < 0 ? -1 : line.IndexOf(FieldSeparator, secondSeparator + 1);
            var fourthSeparator = thirdSeparator < 0 ? -1 : line.IndexOf(FieldSeparator, thirdSeparator + 1);

            if (firstSeparator < 0 || secondSeparator < 0 || thirdSeparator < 0 || fourthSeparator < 0)
            {
                return false;
            }

            var hash = line.Substring(0, firstSeparator);
            var shortHash = line.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1);
            var authorName = line.Substring(secondSeparator + 1, thirdSeparator - secondSeparator - 1);
            var authoredAt = line.Substring(thirdSeparator + 1, fourthSeparator - thirdSeparator - 1);
            var subjectAndParents = line.Substring(fourthSeparator + 1);
            var parentSeparator = subjectAndParents.LastIndexOf(FieldSeparator);

            if (parentSeparator >= 0)
            {
                subject = subjectAndParents.Substring(0, parentSeparator);
                parentHashes = subjectAndParents.Substring(parentSeparator + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                subject = subjectAndParents;
            }

            fields = new[] { hash, shortHash, authorName, authoredAt, subject };
            return true;
        }

        static string[] BuildHistoryArguments(int maxCount)
        {
            return new[]
            {
                "log",
                $"--max-count={NormalizeMaxCount(maxCount)}",
                "--pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s%x1f%P",
                "--date=iso-strict"
            };
        }

        static int NormalizeMaxCount(int maxCount)
        {
            return maxCount > 0 ? maxCount : DefaultMaxCount;
        }

        static bool IsEmptyRepositoryError(GitCommandResult result)
        {
            var combinedMessage = string.Join("\n", new[] { result.ErrorMessage, result.StandardError, result.StandardOutput });
            return combinedMessage.IndexOf("does not have any commits yet", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedMessage.IndexOf("has no commits yet", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedMessage.IndexOf("unknown revision or path not in the working tree", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string ResolveProjectRoot(string projectRootOverride)
        {
            if (!string.IsNullOrWhiteSpace(projectRootOverride))
            {
                return Path.GetFullPath(projectRootOverride);
            }

            var assetsPath = Application.dataPath;
            if (!string.IsNullOrWhiteSpace(assetsPath))
            {
                var assetsDirectory = new DirectoryInfo(assetsPath);
                if (assetsDirectory.Parent != null)
                {
                    return assetsDirectory.Parent.FullName;
                }
            }

            return Directory.GetCurrentDirectory();
        }

        static string NormalizeGitErrorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return "Git history could not be loaded.";
            }

            var trimmed = errorMessage.Trim();
            if (trimmed.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "No Git repository was found at or above the Unity project root.";
            }

            return trimmed;
        }
    }
}
