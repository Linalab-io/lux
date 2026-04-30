using System;
using System.IO;

namespace Linalab.UnityGit.Editor
{
    public static class UnityGitStagingService
    {
        static readonly string[] StageAllArguments = { "add", "--all" };
        static readonly string[] UnstageAllArguments = { "reset", "HEAD", "--", "." };

        public static GitCommandResult StagePath(string repositoryRoot, string path)
        {
            return RunPathCommand(repositoryRoot, path, "add");
        }

        public static GitCommandResult UnstagePath(string repositoryRoot, string path)
        {
            return RunPathCommand(repositoryRoot, path, "reset", "HEAD");
        }

        public static GitCommandResult StageAll(string repositoryRoot)
        {
            return RunRepositoryCommand(repositoryRoot, StageAllArguments);
        }

        public static GitCommandResult UnstageAll(string repositoryRoot)
        {
            return RunRepositoryCommand(repositoryRoot, UnstageAllArguments);
        }

        static GitCommandResult RunPathCommand(string repositoryRoot, string path, params string[] commandPrefix)
        {
            if (!TryResolveRepositoryRoot(repositoryRoot, out var normalizedRepositoryRoot, out var repositoryError))
            {
                return CreateFailureResult(repositoryRoot, repositoryError);
            }

            if (!TryResolveRepositoryRelativePath(normalizedRepositoryRoot, path, out var relativePath, out var pathError))
            {
                return CreateFailureResult(normalizedRepositoryRoot, pathError);
            }

            if (commandPrefix.Length == 1)
            {
                return GitCommandRunner.ExecuteSerialized(normalizedRepositoryRoot, commandPrefix[0], "--", relativePath);
            }

            return GitCommandRunner.ExecuteSerialized(normalizedRepositoryRoot, commandPrefix[0], commandPrefix[1], "--", relativePath);
        }

        static GitCommandResult RunRepositoryCommand(string repositoryRoot, string[] arguments)
        {
            if (!TryResolveRepositoryRoot(repositoryRoot, out var normalizedRepositoryRoot, out var repositoryError))
            {
                return CreateFailureResult(repositoryRoot, repositoryError);
            }

            return GitCommandRunner.ExecuteSerialized(normalizedRepositoryRoot, arguments);
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

        static bool TryResolveRepositoryRelativePath(string repositoryRoot, string path, out string relativePath, out string errorMessage)
        {
            relativePath = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "Path is required.";
                return false;
            }

            string normalizedRepositoryRoot;
            string normalizedFullPath;

            try
            {
                normalizedRepositoryRoot = TrimTrailingSeparators(Path.GetFullPath(repositoryRoot));
                var fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, normalizedRepositoryRoot);
                normalizedFullPath = TrimTrailingSeparators(fullPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException ||
                exception is System.Security.SecurityException)
            {
                errorMessage = "Path could not be resolved inside the repository root.";
                return false;
            }

            var repositoryPrefix = normalizedRepositoryRoot + Path.DirectorySeparatorChar;

            if (!normalizedFullPath.StartsWith(repositoryPrefix, StringComparison.Ordinal) && !string.Equals(normalizedFullPath, normalizedRepositoryRoot, StringComparison.Ordinal))
            {
                errorMessage = "Path must be inside the repository root.";
                return false;
            }

            if (string.Equals(normalizedFullPath, normalizedRepositoryRoot, StringComparison.Ordinal))
            {
                errorMessage = "Path must point to a file or folder within the repository root.";
                return false;
            }

            relativePath = normalizedFullPath.Substring(repositoryPrefix.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            return true;
        }

        static GitCommandResult CreateFailureResult(string workingDirectory, string errorMessage)
        {
            return new GitCommandResult(false, string.Empty, string.Empty, -1, workingDirectory ?? string.Empty, errorMessage ?? string.Empty);
        }

        static string TrimTrailingSeparators(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
