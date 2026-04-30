using Linalab.UnityGit.Editor;
using NUnit.Framework;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityGit.Editor.Tests
{
    public class UnityGitWindowTests
    {
        [Test]
        public void ShowWindow_CreatesWindowWithExpectedTitle()
        {
            var window = ScriptableObject.CreateInstance<UnityGitWindow>();

            try
            {
                var onEnable = typeof(UnityGitWindow).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onEnable, Is.Not.Null);
                onEnable.Invoke(window, null);

                Assert.That(window, Is.Not.Null);
                Assert.That(window.titleContent.text, Is.EqualTo("Unity Git"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void UnityGitWindow_SetOperationMessage_SetsMessageAndType()
        {
            var window = ScriptableObject.CreateInstance<UnityGitWindow>();

            try
            {
                var setOperationMessage = typeof(UnityGitWindow).GetMethod("SetOperationMessage", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(setOperationMessage, Is.Not.Null);

                setOperationMessage.Invoke(window, new object[] { "Test Error", true });

                var messageField = typeof(UnityGitWindow).GetField("_operationMessage", BindingFlags.Instance | BindingFlags.NonPublic);
                var messageTypeField = typeof(UnityGitWindow).GetField("_operationMessageType", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(messageField.GetValue(window), Is.EqualTo("Test Error"));
                Assert.That(messageTypeField.GetValue(window), Is.EqualTo(MessageType.Error));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ParseStatusOutput_ParsesXYColumns()
        {
            const string output = "## milestone/01-editor-git-client...origin/main\nM  Assets/ChangedInIndex.txt\n M Assets/ChangedInWorktree.txt\nMM Assets/ChangedInBoth.txt\nD  Assets/DeletedInIndex.txt\n D Assets/DeletedInWorktree.txt\n?? Assets/Untracked.txt\n";

            var snapshot = UnityGitStatusService.ParseStatusOutput(output, "/repo");

            Assert.That(snapshot.IsRepository, Is.True);
            Assert.That(snapshot.BranchName, Is.EqualTo("milestone/01-editor-git-client"));
            Assert.That(snapshot.Entries.Count, Is.EqualTo(6));

            Assert.That(snapshot.Entries[0].Code, Is.EqualTo("M "));
            Assert.That(snapshot.Entries[0].IndexStatus, Is.EqualTo("M"));
            Assert.That(snapshot.Entries[0].WorktreeStatus, Is.EqualTo(" "));
            Assert.That(snapshot.Entries[0].IsStaged, Is.True);
            Assert.That(snapshot.Entries[0].IsUnstaged, Is.False);
            Assert.That(snapshot.Entries[0].IsUntracked, Is.False);

            Assert.That(snapshot.Entries[1].Code, Is.EqualTo(" M"));
            Assert.That(snapshot.Entries[1].IndexStatus, Is.EqualTo(" "));
            Assert.That(snapshot.Entries[1].WorktreeStatus, Is.EqualTo("M"));
            Assert.That(snapshot.Entries[1].IsStaged, Is.False);
            Assert.That(snapshot.Entries[1].IsUnstaged, Is.True);

            Assert.That(snapshot.Entries[2].Code, Is.EqualTo("MM"));
            Assert.That(snapshot.Entries[2].IsStaged, Is.True);
            Assert.That(snapshot.Entries[2].IsUnstaged, Is.True);

            Assert.That(snapshot.Entries[3].Code, Is.EqualTo("D "));
            Assert.That(snapshot.Entries[3].IsDeleted, Is.True);

            Assert.That(snapshot.Entries[4].Code, Is.EqualTo(" D"));
            Assert.That(snapshot.Entries[4].IsDeleted, Is.True);

            Assert.That(snapshot.Entries[5].Code, Is.EqualTo("??"));
            Assert.That(snapshot.Entries[5].IsUntracked, Is.True);
            Assert.That(snapshot.Entries[5].IsStaged, Is.False);
            Assert.That(snapshot.Entries[5].IsUnstaged, Is.False);
        }

        [Test]
        public void ParseStatusOutput_ParsesRenamedAndConflictedEntries()
        {
            const string output = "## main\nUU Assets/Conflict.txt\nR  Assets/OldName.txt -> Assets/NewName.txt\n";

            var snapshot = UnityGitStatusService.ParseStatusOutput(output, "/repo");

            Assert.That(snapshot.IsRepository, Is.True);
            Assert.That(snapshot.Entries, Has.Count.EqualTo(2));

            Assert.That(snapshot.Entries[0].Code, Is.EqualTo("UU"));
            Assert.That(snapshot.Entries[0].Path, Is.EqualTo("Assets/Conflict.txt"));
            Assert.That(snapshot.Entries[0].IsConflicted, Is.True);

            Assert.That(snapshot.Entries[1].Code, Is.EqualTo("R "));
            Assert.That(snapshot.Entries[1].Path, Is.EqualTo("Assets/NewName.txt"));
            Assert.That(snapshot.Entries[1].OldPath, Is.EqualTo("Assets/OldName.txt"));
            Assert.That(snapshot.Entries[1].IsRenamed, Is.True);
        }

        [Test]
        public void ReadStatus_ResolvesTemporaryRepository()
        {
            using var repository = UnityGitTestRepository.Create();
            var expectedBranchName = repository.ReadGitOutput("symbolic-ref", "--short", "HEAD");

            var snapshot = UnityGitStatusService.ReadStatus(repository.RepositoryPath);

            Assert.That(snapshot.IsRepository, Is.True, snapshot.ErrorMessage);
            Assert.That(snapshot.RepositoryRoot, Is.EqualTo(Path.GetFullPath(repository.RepositoryPath)));
            Assert.That(snapshot.BranchName, Is.EqualTo(expectedBranchName));
        }

        [Test]
        public void ReadStatus_WithRenamedFile_ReportsOldAndNewPath()
        {
            using var repository = UnityGitTestRepository.CreateCommitted("OldName.txt", "old content");
            repository.AssertGitSuccess("mv", "Assets/OldName.txt", "Assets/NewName.txt");

            var snapshot = UnityGitStatusService.ReadStatus(repository.RepositoryPath);

            Assert.That(snapshot.IsRepository, Is.True, snapshot.ErrorMessage);
            Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
            Assert.That(snapshot.Entries[0].IsRenamed, Is.True);
            Assert.That(snapshot.Entries[0].OldPath, Is.EqualTo("Assets/OldName.txt"));
            Assert.That(snapshot.Entries[0].Path, Is.EqualTo("Assets/NewName.txt"));
        }

        [Test]
        public void ReadStatus_WithMissingProjectRoot_ReturnsNonRepositorySnapshot()
        {
            var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unity-git-missing-project-root");
            if (System.IO.Directory.Exists(missingPath))
            {
                System.IO.Directory.Delete(missingPath, true);
            }

            var snapshot = UnityGitStatusService.ReadStatus(missingPath);

            Assert.That(snapshot.IsRepository, Is.False);
            Assert.That(snapshot.RepositoryRoot, Is.Empty);
            Assert.That(snapshot.BranchName, Is.Empty);
            Assert.That(snapshot.ErrorMessage, Is.EqualTo("Unity project root could not be resolved."));
            Assert.That(snapshot.Entries, Is.Empty);
        }

        [Test]
        public void ReadStatus_OutsideRepository_ReturnsFriendlyMessage()
        {
            using var workspace = UnityGitTestRepository.CreateWorkspace("unity-git-non-repo");

            var snapshot = UnityGitStatusService.ReadStatus(workspace.RepositoryPath);

            Assert.That(snapshot.IsRepository, Is.False);
            Assert.That(snapshot.RepositoryRoot, Is.Empty);
            Assert.That(snapshot.BranchName, Is.Empty);
            Assert.That(snapshot.ErrorMessage, Is.EqualTo("No Git repository was found at or above the Unity project root."));
            Assert.That(snapshot.Entries, Is.Empty);
        }

        [Test]
        public void NotifyStatusChanged_RaisesStatusChangedEvent()
        {
            var notifyCount = 0;
            void Handler()
            {
                notifyCount++;
            }

            try
            {
                UnityGitStatusService.StatusChanged += Handler;

                UnityGitStatusService.NotifyStatusChanged();

                Assert.That(notifyCount, Is.EqualTo(1));
            }
            finally
            {
                UnityGitStatusService.StatusChanged -= Handler;
            }
        }

        [Test]
        public void ParseStatusOutput_WithNoEntries_ReturnsCleanSnapshot()
        {
            const string output = "## milestone/01-editor-git-client...origin/main\n";

            var snapshot = UnityGitStatusService.ParseStatusOutput(output, "/repo");

            Assert.That(snapshot.IsRepository, Is.True);
            Assert.That(snapshot.BranchName, Is.EqualTo("milestone/01-editor-git-client"));
            Assert.That(snapshot.RepositoryRoot, Is.EqualTo("/repo"));
            Assert.That(snapshot.Entries, Is.Empty);
        }

        [Test]
        public void ParseStatusOutput_WithUnbornBranch_ReturnsBranchName()
        {
            const string output = "## No commits yet on milestone/01-editor-git-client\n";

            var snapshot = UnityGitStatusService.ParseStatusOutput(output, "/repo");

            Assert.That(snapshot.IsRepository, Is.True);
            Assert.That(snapshot.BranchName, Is.EqualTo("milestone/01-editor-git-client"));
            Assert.That(snapshot.Entries, Is.Empty);
        }

        [Test]
        public void ParseStatusOutput_WithInitialCommitBranch_ReturnsBranchName()
        {
            const string output = "## Initial commit on milestone/01-editor-git-client\n";

            var snapshot = UnityGitStatusService.ParseStatusOutput(output, "/repo");

            Assert.That(snapshot.IsRepository, Is.True);
            Assert.That(snapshot.BranchName, Is.EqualTo("milestone/01-editor-git-client"));
            Assert.That(snapshot.Entries, Is.Empty);
        }

        [TestCase("HEAD (no branch)")]
        [TestCase("HEAD detached at 1a2b3c4")]
        [TestCase("HEAD detached from milestone/01-editor-git-client")]
        public void ParseStatusOutput_WithDetachedHead_PreservesStateDescription(string branchState)
        {
            var output = $"## {branchState}\n";

            var snapshot = UnityGitStatusService.ParseStatusOutput(output, "/repo");

            Assert.That(snapshot.IsRepository, Is.True);
            Assert.That(snapshot.BranchName, Is.EqualTo(branchState));
            Assert.That(snapshot.Entries, Is.Empty);
        }

    }
}
