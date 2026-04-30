using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Linalab.UnityGit.Editor
{
    public readonly struct GitStatusEntry
    {
        public GitStatusEntry(string code, string path)
            : this(code != null && code.Length > 0 ? code[0].ToString() : string.Empty, code != null && code.Length > 1 ? code[1].ToString() : string.Empty, path, string.Empty)
        {
        }

        public GitStatusEntry(string indexStatus, string worktreeStatus, string path, string oldPath = "")
        {
            IndexStatus = indexStatus ?? string.Empty;
            WorktreeStatus = worktreeStatus ?? string.Empty;
            Path = path ?? string.Empty;
            OldPath = oldPath ?? string.Empty;
            Code = string.Concat(ResolveStatusChar(IndexStatus), ResolveStatusChar(WorktreeStatus));
        }

        public string Code { get; }
        public string Path { get; }
        public string IndexStatus { get; }
        public string WorktreeStatus { get; }
        public string OldPath { get; }

        public bool IsStaged => IsStatusChanged(IndexStatus);
        public bool IsUnstaged => IsStatusChanged(WorktreeStatus);
        public bool IsUntracked => IndexStatus == "?" && WorktreeStatus == "?";
        public bool IsDeleted => IsDeletedStatus(IndexStatus) || IsDeletedStatus(WorktreeStatus);
        public bool IsRenamed => IsRenameStatus(IndexStatus) || IsRenameStatus(WorktreeStatus);
        public bool IsConflicted => IsConflictStatus(Code);

        static string ResolveStatusChar(string status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return " ";
            }

            return status.Length > 0 ? status.Substring(0, 1) : " ";
        }

        static bool IsStatusChanged(string status)
        {
            return !string.IsNullOrEmpty(status) && status != " " && status != "?";
        }

        static bool IsDeletedStatus(string status)
        {
            return status == "D";
        }

        static bool IsRenameStatus(string status)
        {
            return status == "R";
        }

        static bool IsConflictStatus(string code)
        {
            switch (code)
            {
                case "UU":
                case "AA":
                case "DD":
                case "AU":
                case "UA":
                case "DU":
                case "UD":
                    return true;
                default:
                    return false;
            }
        }
    }

    public sealed class GitStatusSnapshot
    {
        public GitStatusSnapshot(bool isRepository, string repositoryRoot, string branchName, string errorMessage, IReadOnlyList<GitStatusEntry> entries)
        {
            IsRepository = isRepository;
            RepositoryRoot = repositoryRoot ?? string.Empty;
            BranchName = branchName ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            Entries = entries ?? Array.Empty<GitStatusEntry>();
        }

        public bool IsRepository { get; }
        public string RepositoryRoot { get; }
        public string BranchName { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<GitStatusEntry> Entries { get; }
    }

    public static class UnityGitStatusService
    {
        public static event Action StatusChanged;

        public static void NotifyStatusChanged()
        {
            StatusChanged?.Invoke();
        }

        static readonly string[] StatusArguments = { "status", "--short", "--branch", "--untracked-files=all" };
        static readonly string[] ResolveRepoRootArguments = { "rev-parse", "--show-toplevel" };

        public static GitStatusSnapshot ReadStatus(string projectRootOverride = null)
        {
            var projectRoot = ResolveProjectRoot(projectRootOverride);
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return new GitStatusSnapshot(false, string.Empty, string.Empty, "Unity project root could not be resolved.", Array.Empty<GitStatusEntry>());
            }

            var repoRootResult = GitCommandRunner.Execute(projectRoot, ResolveRepoRootArguments);
            if (!repoRootResult.Success)
            {
                return new GitStatusSnapshot(false, string.Empty, string.Empty, NormalizeGitErrorMessage(repoRootResult.ErrorMessage), Array.Empty<GitStatusEntry>());
            }

            var repoRoot = repoRootResult.Output.Trim();
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            {
                return new GitStatusSnapshot(false, string.Empty, string.Empty, "Git repository root could not be resolved.", Array.Empty<GitStatusEntry>());
            }

            var statusResult = GitCommandRunner.Execute(repoRoot, StatusArguments);
            if (!statusResult.Success)
            {
                return new GitStatusSnapshot(false, repoRoot, string.Empty, NormalizeGitErrorMessage(statusResult.ErrorMessage), Array.Empty<GitStatusEntry>());
            }

            return ParseStatusOutput(statusResult.Output, repoRoot);
        }

        public static GitStatusSnapshot ParseStatusOutput(string output, string repositoryRoot)
        {
            var normalizedOutput = output ?? string.Empty;
            var lines = normalizedOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var branchName = string.Empty;
            var entries = new List<GitStatusEntry>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    branchName = ParseBranchName(line.Substring(3));
                    continue;
                }

                if (line.Length < 3)
                {
                    continue;
                }

                var indexStatus = line.Substring(0, 1);
                var worktreeStatus = line.Substring(1, 1);
                var path = line.Substring(3).Trim();
                var oldPath = string.Empty;

                var renameSeparatorIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (renameSeparatorIndex >= 0)
                {
                    oldPath = path.Substring(0, renameSeparatorIndex).Trim();
                    path = path.Substring(renameSeparatorIndex + 4).Trim();
                }

                entries.Add(new GitStatusEntry(indexStatus, worktreeStatus, path, oldPath));
            }

            return new GitStatusSnapshot(true, repositoryRoot, branchName, string.Empty, entries);
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

        static string ParseBranchName(string branchLine)
        {
            if (string.IsNullOrWhiteSpace(branchLine))
            {
                return string.Empty;
            }

            var trimmed = branchLine.Trim();
            const string noCommitsYetPrefix = "No commits yet on ";
            const string initialCommitPrefix = "Initial commit on ";

            if (trimmed.StartsWith(noCommitsYetPrefix, StringComparison.Ordinal))
            {
                return trimmed.Substring(noCommitsYetPrefix.Length).Trim();
            }

            if (trimmed.StartsWith(initialCommitPrefix, StringComparison.Ordinal))
            {
                return trimmed.Substring(initialCommitPrefix.Length).Trim();
            }

            if (trimmed.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var aheadSeparatorIndex = trimmed.IndexOf("...", StringComparison.Ordinal);
            if (aheadSeparatorIndex >= 0)
            {
                return trimmed.Substring(0, aheadSeparatorIndex).Trim();
            }

            var stateSeparatorIndex = trimmed.IndexOf(' ');
            if (stateSeparatorIndex >= 0)
            {
                return trimmed.Substring(0, stateSeparatorIndex).Trim();
            }

            return trimmed;
        }

        static string NormalizeGitErrorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return "Git status could not be loaded.";
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
