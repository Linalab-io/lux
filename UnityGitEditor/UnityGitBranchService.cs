using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityGit.Editor
{
    public readonly struct GitBranchEntry
    {
        public GitBranchEntry(string name, bool isCurrent)
        {
            Name = name ?? string.Empty;
            IsCurrent = isCurrent;
        }

        public string Name { get; }
        public bool IsCurrent { get; }
    }

    public sealed class GitBranchSnapshot
    {
        public GitBranchSnapshot(bool isRepository, string repositoryRoot, string currentBranchName, string errorMessage, IReadOnlyList<GitBranchEntry> branches)
        {
            IsRepository = isRepository;
            RepositoryRoot = repositoryRoot ?? string.Empty;
            CurrentBranchName = currentBranchName ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            Branches = branches ?? Array.Empty<GitBranchEntry>();
        }

        public bool IsRepository { get; }
        public string RepositoryRoot { get; }
        public string CurrentBranchName { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<GitBranchEntry> Branches { get; }
    }

    public static class UnityGitBranchService
    {
        static readonly string[] ResolveRepoRootArguments = { "rev-parse", "--show-toplevel" };
        static readonly string[] BranchListArguments = { "branch", "--format=%(refname:short)" };
        static readonly string[] CurrentBranchArguments = { "branch", "--show-current" };

        public static GitBranchSnapshot ReadBranches(string projectRootOverride = null)
        {
            var projectRoot = ResolveProjectRoot(projectRootOverride);
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return new GitBranchSnapshot(false, string.Empty, string.Empty, "Unity project root could not be resolved.", Array.Empty<GitBranchEntry>());
            }

            var repoRootResult = GitCommandRunner.Execute(projectRoot, ResolveRepoRootArguments);
            if (!repoRootResult.Success)
            {
                return new GitBranchSnapshot(false, string.Empty, string.Empty, NormalizeGitErrorMessage(repoRootResult.ErrorMessage), Array.Empty<GitBranchEntry>());
            }

            var repoRoot = repoRootResult.Output.Trim();
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            {
                return new GitBranchSnapshot(false, string.Empty, string.Empty, "Git repository root could not be resolved.", Array.Empty<GitBranchEntry>());
            }

            var branchListResult = GitCommandRunner.Execute(repoRoot, BranchListArguments);
            if (!branchListResult.Success)
            {
                return new GitBranchSnapshot(false, repoRoot, string.Empty, NormalizeGitErrorMessage(branchListResult.ErrorMessage), Array.Empty<GitBranchEntry>());
            }

            var currentBranchName = ReadCurrentBranchName(projectRoot, repoRoot);
            var branches = ParseBranchList(branchListResult.Output, currentBranchName);

            return new GitBranchSnapshot(true, repoRoot, currentBranchName, string.Empty, branches);
        }

        public static GitCommandResult SwitchBranch(string repositoryRoot, string branchName)
        {
            if (!TryResolveRepositoryRoot(repositoryRoot, out var normalizedRepositoryRoot, out var repositoryError))
            {
                return CreateFailureResult(repositoryRoot, repositoryError);
            }

            if (string.IsNullOrWhiteSpace(branchName))
            {
                return CreateFailureResult(normalizedRepositoryRoot, "Branch name is required.");
            }

            var switchResult = GitCommandRunner.ExecuteSerialized(normalizedRepositoryRoot, "switch", branchName);
            if (switchResult.Success)
            {
                return switchResult;
            }

            if (!IsSwitchUnsupported(switchResult))
            {
                return switchResult;
            }

            var checkoutResult = GitCommandRunner.ExecuteSerialized(normalizedRepositoryRoot, "checkout", branchName);
            return checkoutResult;
        }

        static IReadOnlyList<GitBranchEntry> ParseBranchList(string output, string currentBranchName)
        {
            var normalizedOutput = output ?? string.Empty;
            var lines = normalizedOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var branches = new List<GitBranchEntry>();

            foreach (var line in lines)
            {
                var branchName = line?.Trim();
                if (string.IsNullOrWhiteSpace(branchName))
                {
                    continue;
                }

                branches.Add(new GitBranchEntry(branchName, string.Equals(branchName, currentBranchName, StringComparison.Ordinal)));
            }

            return branches;
        }

        static string ReadCurrentBranchName(string projectRoot, string repositoryRoot)
        {
            var showCurrentResult = GitCommandRunner.Execute(repositoryRoot, CurrentBranchArguments);
            if (showCurrentResult.Success)
            {
                var currentBranch = showCurrentResult.Output.Trim();
                if (!string.IsNullOrWhiteSpace(currentBranch))
                {
                    return currentBranch;
                }
            }

            var statusSnapshot = UnityGitStatusService.ReadStatus(projectRoot);
            return statusSnapshot != null ? statusSnapshot.BranchName : string.Empty;
        }

        static bool TryResolveRepositoryRoot(string repositoryRoot, out string normalizedRepositoryRoot, out string errorMessage)
        {
            normalizedRepositoryRoot = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(repositoryRoot))
            {
                errorMessage = "Repository root is required.";
                return false;
            }

            try
            {
                normalizedRepositoryRoot = Path.GetFullPath(repositoryRoot);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException ||
                exception is System.Security.SecurityException)
            {
                errorMessage = "Repository root could not be resolved.";
                return false;
            }

            if (!Directory.Exists(normalizedRepositoryRoot))
            {
                errorMessage = "Repository root does not exist.";
                return false;
            }

            var repoRootResult = GitCommandRunner.Execute(normalizedRepositoryRoot, "rev-parse", "--show-toplevel");
            if (!repoRootResult.Success)
            {
                errorMessage = string.IsNullOrWhiteSpace(repoRootResult.ErrorMessage)
                    ? "Repository root is not inside a Git worktree."
                    : repoRootResult.ErrorMessage;
                return false;
            }

            var resolvedGitRoot = TrimTrailingSeparators(Path.GetFullPath(repoRootResult.Output.Trim()));
            if (!string.Equals(normalizedRepositoryRoot, resolvedGitRoot, StringComparison.Ordinal))
            {
                errorMessage = "Repository root must be the Git worktree root.";
                return false;
            }

            return true;
        }

        static GitCommandResult CreateFailureResult(string workingDirectory, string errorMessage)
        {
            return new GitCommandResult(false, string.Empty, string.Empty, -1, workingDirectory ?? string.Empty, errorMessage ?? string.Empty);
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

        static bool IsSwitchUnsupported(GitCommandResult switchResult)
        {
            var combinedMessage = string.Join("\n", new[] { switchResult.ErrorMessage, switchResult.StandardError, switchResult.StandardOutput });
            return combinedMessage.IndexOf("is not a git command", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedMessage.IndexOf("unknown subcommand", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedMessage.IndexOf("unknown command", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string NormalizeGitErrorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return "Git branches could not be loaded.";
            }

            var trimmed = errorMessage.Trim();
            if (trimmed.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "No Git repository was found at or above the Unity project root.";
            }

            return trimmed;
        }

        static string TrimTrailingSeparators(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
