using System;
using System.IO;
using UnityEditor;

namespace Linalab.UnityGit.Editor
{
    public readonly struct GitRemoteOperationResult
    {
        public GitRemoteOperationResult(GitCommandResult commandResult, bool shouldRefreshStatus)
        {
            CommandResult = commandResult;
            ShouldRefreshStatus = shouldRefreshStatus;
        }

        public GitCommandResult CommandResult { get; }
        public bool ShouldRefreshStatus { get; }
        public bool Success => CommandResult.Success;
        public string StandardOutput => CommandResult.StandardOutput;
        public string StandardError => CommandResult.StandardError;
        public int ExitCode => CommandResult.ExitCode;
        public string WorkingDirectory => CommandResult.WorkingDirectory;
        public string ErrorMessage => CommandResult.ErrorMessage;
        public string Output => CommandResult.Output;
    }

    public static class UnityGitRemoteService
    {
        static readonly string[] PullCurrentBranchArguments = { "pull", "--ff-only" };
        static readonly string[] PushCurrentBranchArguments = { "push" };

        public static Action RefreshProjectAssets = AssetDatabase.Refresh;
        public static Action RefreshStatus = () => { };

        public static GitRemoteOperationResult PullCurrentBranch(string repositoryRoot)
        {
            if (!TryResolveRepositoryRoot(repositoryRoot, out var normalizedRepositoryRoot, out var repositoryError))
            {
                return CreateFailureResult(normalizedRepositoryRoot, repositoryError);
            }

            var pullResult = GitCommandRunner.ExecuteSerialized(normalizedRepositoryRoot, PullCurrentBranchArguments);
            if (!pullResult.Success)
            {
                return new GitRemoteOperationResult(pullResult, false);
            }

            RefreshProjectAssets();
            RefreshStatus();
            return new GitRemoteOperationResult(pullResult, true);
        }

        public static GitRemoteOperationResult PushCurrentBranch(string repositoryRoot)
        {
            if (!TryResolveRepositoryRoot(repositoryRoot, out var normalizedRepositoryRoot, out var repositoryError))
            {
                return CreateFailureResult(normalizedRepositoryRoot, repositoryError);
            }

            var pushResult = GitCommandRunner.ExecuteSerialized(normalizedRepositoryRoot, PushCurrentBranchArguments);
            if (!pushResult.Success)
            {
                return new GitRemoteOperationResult(pushResult, false);
            }

            RefreshStatus();
            return new GitRemoteOperationResult(pushResult, true);
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

        static GitRemoteOperationResult CreateFailureResult(string workingDirectory, string errorMessage)
        {
            return new GitRemoteOperationResult(new GitCommandResult(false, string.Empty, string.Empty, -1, workingDirectory ?? string.Empty, errorMessage ?? string.Empty), false);
        }

        static string TrimTrailingSeparators(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
