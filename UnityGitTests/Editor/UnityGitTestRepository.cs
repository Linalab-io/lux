using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace Linalab.UnityGit.Editor.Tests
{
    internal sealed class UnityGitTestRepository : IDisposable
    {
        const string TestUserName = "Unity Git Tests";
        const string TestUserEmail = "unity-git-tests@example.com";

        bool _disposed;

        UnityGitTestRepository(string repositoryPath)
        {
            RepositoryPath = repositoryPath;
        }

        public string RepositoryPath { get; }

        public static UnityGitTestRepository Create(string prefix = "unity-git-test-repo")
        {
            var repository = CreateWorkspace(prefix);
            repository.AssertGitSuccess("init");
            repository.ConfigureUser();
            return repository;
        }

        public static UnityGitTestRepository CreateCommitted(string fileName, string initialContents, string initialCommitMessage = "base")
        {
            return CreateCommitted(new Dictionary<string, string>
            {
                { fileName, initialContents }
            }, initialCommitMessage);
        }

        public static UnityGitTestRepository CreateCommitted(IReadOnlyDictionary<string, string> files, string initialCommitMessage = "base")
        {
            var repository = Create();

            foreach (var pair in files)
            {
                repository.WriteFile(Path.Combine("Assets", pair.Key), pair.Value);
            }

            repository.AssertGitSuccess("add", "--all");
            repository.AssertGitSuccess("commit", "-m", initialCommitMessage);

            return repository;
        }

        public static UnityGitTestRepository CreateWorkspace(string prefix = "unity-git-test-workspace")
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);
            return new UnityGitTestRepository(repositoryPath);
        }

        public string CreateBareRepository(string directoryName = "origin.git")
        {
            var originPath = GetPath(directoryName);
            AssertGitSuccess("init", "--bare", originPath);
            return originPath;
        }

        public string CloneRepository(string originPath, string directoryName)
        {
            var clonePath = GetPath(directoryName);
            AssertGitSuccess("clone", originPath, clonePath);
            ConfigureUser(clonePath);
            return clonePath;
        }

        public void ConfigureUser()
        {
            ConfigureUser(RepositoryPath);
        }

        public string GetPath(params string[] relativeParts)
        {
            var fullPath = RepositoryPath;

            foreach (var part in relativeParts)
            {
                fullPath = Path.Combine(fullPath, part);
            }

            return fullPath;
        }

        public string WriteFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(RepositoryPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public string ReadGitOutput(params string[] arguments)
        {
            return ReadGitOutput(RepositoryPath, arguments);
        }

        public void AssertGitSuccess(params string[] arguments)
        {
            AssertGitSuccess(RepositoryPath, arguments);
        }

        public static void ConfigureUser(string repositoryPath)
        {
            AssertGitSuccess(repositoryPath, "config", "user.name", TestUserName);
            AssertGitSuccess(repositoryPath, "config", "user.email", TestUserEmail);
        }

        public static string WriteFile(string repositoryPath, string relativePath, string contents)
        {
            var fullPath = Path.Combine(repositoryPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public static string ReadGitOutput(string workingDirectory, params string[] arguments)
        {
            var result = GitCommandRunner.Execute(workingDirectory, arguments);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.Output.Trim();
        }

        public static void AssertGitSuccess(string workingDirectory, params string[] arguments)
        {
            var result = GitCommandRunner.Execute(workingDirectory, arguments);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (Directory.Exists(RepositoryPath))
            {
                Directory.Delete(RepositoryPath, true);
            }
        }
    }
}
