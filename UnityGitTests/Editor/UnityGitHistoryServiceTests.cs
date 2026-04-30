using Linalab.UnityGit.Editor;
using NUnit.Framework;
using System;

namespace Linalab.UnityGit.Editor.Tests
{
    public class UnityGitHistoryServiceTests
    {
        [Test]
        public void ReadHistory_WithTwoCommitsAndMaxCountOne_ReturnsLatestCommitOnly()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("history-source.txt", "first content", "first commit");
            repository.WriteFile("Assets/history-source.txt", "second content");
            repository.AssertGitSuccess("add", "--all");
            repository.AssertGitSuccess("commit", "-m", "second commit");

            var snapshot = UnityGitHistoryService.ReadHistory(repository.RepositoryPath, 1);
            var latestCommitHash = repository.ReadGitOutput("rev-parse", "HEAD");
            var latestCommitShortHash = repository.ReadGitOutput("rev-parse", "--short", "HEAD");

            Assert.That(snapshot.IsRepository, Is.True, snapshot.ErrorMessage);
            Assert.That(snapshot.ErrorMessage, Is.Empty);
            Assert.That(snapshot.FriendlyMessage, Is.Empty);
            Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
            Assert.That(snapshot.MaxCount, Is.EqualTo(1));

            var entry = snapshot.Entries[0];
            Assert.That(entry.Hash, Is.EqualTo(latestCommitHash));
            Assert.That(entry.ShortHash, Is.EqualTo(latestCommitShortHash));
            Assert.That(entry.AuthorName, Is.EqualTo("Unity Git Tests"));
            Assert.That(entry.Subject, Is.EqualTo("second commit"));
            Assert.That(entry.AuthoredAt, Is.Not.EqualTo(default(DateTimeOffset)));
        }

        [Test]
        public void ReadHistory_WithInvalidMaxCount_UsesDefaultMaximum()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("history-source.txt", "first content", "first commit");
            repository.WriteFile("Assets/history-source.txt", "second content");
            repository.AssertGitSuccess("add", "--all");
            repository.AssertGitSuccess("commit", "-m", "second commit");

            var snapshot = UnityGitHistoryService.ReadHistory(repository.RepositoryPath, 0);

            Assert.That(snapshot.IsRepository, Is.True, snapshot.ErrorMessage);
            Assert.That(snapshot.ErrorMessage, Is.Empty);
            Assert.That(snapshot.FriendlyMessage, Is.Empty);
            Assert.That(snapshot.MaxCount, Is.EqualTo(50));
            Assert.That(snapshot.Entries, Has.Count.EqualTo(2));
        }

        [Test]
        public void ParseHistoryOutput_WithMalformedAndDelimitedSubject_IgnoresMalformedRowsAndPreservesSubject()
        {
            const string separator = "\u001f";
            var output = string.Join(
                "\n",
                "malformed row without separators",
                $"hash{separator}short{separator}Author Name{separator}2026-04-28T12:34:56+00:00{separator}subject{separator}with{separator}separator{separator}parent1 parent2");

            var snapshot = UnityGitHistoryService.ParseHistoryOutput(output, "/tmp/repository", 50);

            Assert.That(snapshot.IsRepository, Is.True);
            Assert.That(snapshot.ErrorMessage, Is.Empty);
            Assert.That(snapshot.FriendlyMessage, Is.Empty);
            Assert.That(snapshot.Entries, Has.Count.EqualTo(1));

            var entry = snapshot.Entries[0];
            Assert.That(entry.Hash, Is.EqualTo("hash"));
            Assert.That(entry.ShortHash, Is.EqualTo("short"));
            Assert.That(entry.AuthorName, Is.EqualTo("Author Name"));
            Assert.That(entry.Subject, Is.EqualTo($"subject{separator}with{separator}separator"));
            Assert.That(entry.ParentHashes, Is.EqualTo(new[] { "parent1", "parent2" }));
            Assert.That(entry.AuthoredAt, Is.Not.EqualTo(default(DateTimeOffset)));
        }

        [Test]
        public void ReadHistory_OnEmptyRepository_ReturnsEmptyHistoryWithFriendlyMessage()
        {
            using var repository = UnityGitTestRepository.Create();

            var snapshot = UnityGitHistoryService.ReadHistory(repository.RepositoryPath);

            Assert.That(snapshot.IsRepository, Is.True, snapshot.ErrorMessage);
            Assert.That(snapshot.Entries, Is.Empty);
            Assert.That(snapshot.ErrorMessage, Is.Empty);
            Assert.That(snapshot.FriendlyMessage, Is.EqualTo("This repository has no commits yet."));
        }

        [Test]
        public void ReadHistory_OutsideRepository_ReturnsFriendlyMessage()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace();

            var snapshot = UnityGitHistoryService.ReadHistory(workspace.RepositoryPath);

            Assert.That(snapshot.IsRepository, Is.False);
            Assert.That(snapshot.RepositoryRoot, Is.Empty);
            Assert.That(snapshot.ErrorMessage, Is.EqualTo("No Git repository was found at or above the Unity project root."));
            Assert.That(snapshot.FriendlyMessage, Is.EqualTo("No Git repository was found at or above the Unity project root."));
            Assert.That(snapshot.Entries, Is.Empty);
        }
    }
}
