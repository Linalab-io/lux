using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityGit.Editor
{
    public sealed class UnityGitWindow : EditorWindow
    {
        private const string WindowTitle = "Lux Git";

        private GitStatusSnapshot _statusSnapshot;
        private GitBranchSnapshot _branchSnapshot;
        private GitHistorySnapshot _historySnapshot;
        private GitSubmoduleSnapshot _submoduleSnapshot;

        private Vector2 _scrollPosition;
        private bool _isOperationRunning;
        private string _operationMessage;
        private MessageType _operationMessageType;
        private string _selectedBranchName;

        [MenuItem("Window/Linalab/Lux/Unity Git")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityGitWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(420f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            UnityGitRemoteService.RefreshProjectAssets = () => EditorApplication.delayCall += AssetDatabase.Refresh;
            UnityGitRemoteService.RefreshStatus = () => EditorApplication.delayCall += RefreshAll;
            RefreshAll();
            Selection.selectionChanged += OnSelectionChanged;
            UnityGitStatusService.StatusChanged -= OnStatusChanged;
            UnityGitStatusService.StatusChanged += OnStatusChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            UnityGitStatusService.StatusChanged -= OnStatusChanged;
        }

        private void OnSelectionChanged()
        {
            Repaint();
        }

        private void OnStatusChanged()
        {
            EditorApplication.delayCall += RefreshAll;
        }

        private int _refreshGeneration;

        private static string GetProjectRoot()
        {
            if (string.IsNullOrEmpty(Application.dataPath))
            {
                return string.Empty;
            }

            var assetsDirectory = new System.IO.DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? string.Empty : assetsDirectory.Parent.FullName;
        }

        private void RefreshAll()
        {
            var generation = ++_refreshGeneration;
            var projectRoot = GetProjectRoot();

            System.Threading.Tasks.Task.Run(() =>
            {
                var status = UnityGitStatusService.ReadStatus(projectRoot);
                var branch = UnityGitBranchService.ReadBranches(projectRoot);
                var history = UnityGitHistoryService.ReadHistory(projectRoot);
                var submodules = UnityGitSubmoduleService.ReadSubmodules(projectRoot);

                EditorApplication.delayCall += () =>
                {
                    if (generation != _refreshGeneration) return;
                    _statusSnapshot = status;
                    _branchSnapshot = branch;
                    _historySnapshot = history;
                    _submoduleSnapshot = submodules;
                    Repaint();
                };
            });
        }

        private void OnGUI()
        {
            Styles.Initialize();

            DrawHeader();

            if (_statusSnapshot == null)
            {
                DrawMessage("Git status has not been loaded yet.", MessageType.Info);
                return;
            }

            if (!_statusSnapshot.IsRepository)
            {
                DrawMessage(_statusSnapshot.ErrorMessage, MessageType.Warning);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawRepositorySummary();
            DrawOperationStatus();

            EditorGUI.BeginDisabledGroup(_isOperationRunning);

            DrawStagingControls();
            DrawChangedFilesPanel();
            DrawSubmodulePanel();
            DrawBranchControls();
            DrawRemoteControls();
            DrawHistoryPanel();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Lux Git", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUI.BeginDisabledGroup(_isOperationRunning);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    RefreshAll();
                    ClearOperationMessage();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.Space();
        }

        private void DrawMessage(string message, MessageType type)
        {
            if (string.IsNullOrEmpty(message)) return;
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8f);
                EditorGUILayout.HelpBox(message, type);
                GUILayout.Space(8f);
            }
        }

        private void DrawRepositorySummary()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8f);
                using (new EditorGUILayout.VerticalScope(Styles.SummaryBox))
                {
                    DrawSummaryLine("Branch", string.IsNullOrWhiteSpace(_statusSnapshot.BranchName) ? "Unknown" : _statusSnapshot.BranchName);
                    DrawSummaryLine("Repository", _statusSnapshot.RepositoryRoot);
                    DrawSummaryLine("Changed Files", _statusSnapshot.Entries.Count.ToString());
                }
                GUILayout.Space(8f);
            }
        }

        private void DrawSummaryLine(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, Styles.SummaryLabel);
                EditorGUILayout.LabelField(value, Styles.SummaryValue);
            }
        }

        private void DrawOperationStatus()
        {
            if (!string.IsNullOrEmpty(_operationMessage))
            {
                DrawMessage(_operationMessage, _operationMessageType);
            }
        }

        private void SetOperationMessage(string message, bool isError)
        {
            _operationMessage = message;
            _operationMessageType = isError ? MessageType.Error : MessageType.Info;
        }

        private void ClearOperationMessage()
        {
            _operationMessage = string.Empty;
        }

        private void RunOperationBackground(Func<(bool success, string errorMessage)> operation, string successMessage)
        {
            if (_isOperationRunning) return;

            _isOperationRunning = true;
            ClearOperationMessage();
            Repaint();

            System.Threading.Tasks.Task.Run(() =>
            {
                (bool success, string errorMessage) result = (false, string.Empty);
                Exception error = null;
                try
                {
                    result = operation();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (error != null)
                        {
                            SetOperationMessage(error.Message, true);
                        }
                        else
                        {
                            SetOperationMessage(result.success ? successMessage : result.errorMessage, !result.success);
                            if (result.success)
                            {
                                UnityGitStatusService.NotifyStatusChanged();
                            }
                        }
                        _isOperationRunning = false;
                        RefreshAll();
                    };
                }
            });
        }

        private void SwitchBranchAsync(string branchName)
        {
            RunOperationBackground(() =>
            {
                var result = UnityGitBranchService.SwitchBranch(_statusSnapshot.RepositoryRoot, branchName);
                if (result.Success)
                {
                    EditorApplication.delayCall += AssetDatabase.Refresh;
                }
                return (result.Success, result.ErrorMessage);
            }, $"Switched to branch {branchName}.");
        }

        private void DrawStagingControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Staging", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Stage All"))
                {
                    RunOperationBackground(() =>
                    {
                        var result = UnityGitStagingService.StageAll(_statusSnapshot.RepositoryRoot);
                        return (result.Success, result.ErrorMessage);
                    }, "Staged all changes.");
                }
                if (GUILayout.Button("Unstage All"))
                {
                    RunOperationBackground(() =>
                    {
                        var result = UnityGitStagingService.UnstageAll(_statusSnapshot.RepositoryRoot);
                        return (result.Success, result.ErrorMessage);
                    }, "Unstaged all changes.");
                }
            }

            var selectedGuids = Selection.assetGUIDs;
            if (selectedGuids != null && selectedGuids.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Selected Assets ({selectedGuids.Length})", EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Stage Selected"))
                    {
                        var paths = selectedGuids.Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Select(p => GetRepositoryRelativeAssetPath(p, _statusSnapshot.RepositoryRoot))
                            .Where(p => !string.IsNullOrEmpty(p))
                            .ToArray();
                        RunOperationBackground(() =>
                        {
                            bool anyError = false;
                            string lastError = "";
                            foreach (var path in paths)
                            {
                                var result = UnityGitStagingService.StagePath(_statusSnapshot.RepositoryRoot, path);
                                if (!result.Success)
                                {
                                    anyError = true;
                                    lastError = result.ErrorMessage;
                                }
                            }
                            return (!anyError, anyError ? $"Error staging some files: {lastError}" : "");
                        }, "Staged selected files.");
                    }
                    if (GUILayout.Button("Unstage Selected"))
                    {
                        var paths = selectedGuids.Select(AssetDatabase.GUIDToAssetPath)
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Select(p => GetRepositoryRelativeAssetPath(p, _statusSnapshot.RepositoryRoot))
                            .Where(p => !string.IsNullOrEmpty(p))
                            .ToArray();
                        RunOperationBackground(() =>
                        {
                            bool anyError = false;
                            string lastError = "";
                            foreach (var path in paths)
                            {
                                var result = UnityGitStagingService.UnstagePath(_statusSnapshot.RepositoryRoot, path);
                                if (!result.Success)
                                {
                                    anyError = true;
                                    lastError = result.ErrorMessage;
                                }
                            }
                            return (!anyError, anyError ? $"Error unstaging some files: {lastError}" : "");
                        }, "Unstaged selected files.");
                    }
                }
            }
        }

        private void DrawChangedFilesPanel()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Changed Files", EditorStyles.boldLabel);

            if (_statusSnapshot == null || _statusSnapshot.Entries.Count == 0)
            {
                EditorGUILayout.LabelField("No changed files.");
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8f);
                using (new EditorGUILayout.VerticalScope(Styles.FileListBackground))
                {
                    bool isAlternate = false;
                    foreach (var entry in _statusSnapshot.Entries)
                    {
                        DrawChangedFileEntry(entry, isAlternate);
                        isAlternate = !isAlternate;
                    }
                }
                GUILayout.Space(8f);
            }
        }

        private static string GetRepositoryRelativeAssetPath(string assetPath, string repositoryRoot)
        {
            var projectRoot = GetProjectRoot();
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(repositoryRoot) || string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            var fullAssetPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            var fullRepositoryRoot = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(fullAssetPath, fullRepositoryRoot, StringComparison.Ordinal)
                && !fullAssetPath.StartsWith(fullRepositoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !fullAssetPath.StartsWith(fullRepositoryRoot + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var relativePath = fullAssetPath.Substring(fullRepositoryRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Replace('\\', '/');
        }

        private void DrawChangedFileEntry(GitStatusEntry entry, bool isAlternate)
        {
            var rect = EditorGUILayout.BeginHorizontal(Styles.FileEntry);

            if (Event.current.type == EventType.Repaint)
            {
                if (isAlternate)
                {
                    EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.05f));
                }
            }

            EditorGUILayout.LabelField(entry.Code, Styles.StatusCode);
            EditorGUILayout.LabelField(entry.Path, Styles.FilePath);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSubmodulePanel()
        {
            if (_submoduleSnapshot == null || _submoduleSnapshot.Entries.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Submodules", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Init All"))
                {
                    RunOperationBackground(() =>
                    {
                        var result = UnityGitSubmoduleService.InitSubmodules(_statusSnapshot.RepositoryRoot);
                        return (result.Success, result.ErrorMessage);
                    }, "Initialized submodules.");
                }
                if (GUILayout.Button("Update All"))
                {
                    RunOperationBackground(() =>
                    {
                        var result = UnityGitSubmoduleService.UpdateSubmodules(_statusSnapshot.RepositoryRoot);
                        return (result.Success, result.ErrorMessage);
                    }, "Updated submodules.");
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8f);
                using (new EditorGUILayout.VerticalScope(Styles.FileListBackground))
                {
                    bool isAlternate = false;
                    foreach (var entry in _submoduleSnapshot.Entries)
                    {
                        DrawSubmoduleEntry(entry, isAlternate);
                        isAlternate = !isAlternate;
                    }
                }
                GUILayout.Space(8f);
            }
        }

        private void DrawSubmoduleEntry(GitSubmoduleEntry entry, bool isAlternate)
        {
            var rect = EditorGUILayout.BeginHorizontal(Styles.FileEntry);

            if (Event.current.type == EventType.Repaint)
            {
                if (isAlternate)
                {
                    EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.05f));
                }
            }

            EditorGUILayout.LabelField(entry.StatusChar, Styles.StatusCode);
            EditorGUILayout.LabelField(entry.Path, Styles.FilePath);
            
            if (GUILayout.Button("Update", GUILayout.Width(60f)))
            {
                RunOperationBackground(() =>
                {
                    var result = UnityGitSubmoduleService.UpdateSubmodule(_statusSnapshot.RepositoryRoot, entry.Path);
                    return (result.Success, result.ErrorMessage);
                }, $"Updated submodule {entry.Path}.");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBranchControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Branch", EditorStyles.boldLabel);

            if (_branchSnapshot != null && _branchSnapshot.Branches.Count > 0)
            {
                var branchNames = _branchSnapshot.Branches.Select(b => b.Name).ToArray();

                int currentIndex = Array.IndexOf(branchNames, _selectedBranchName);
                if (currentIndex < 0)
                {
                    currentIndex = Array.IndexOf(branchNames, _branchSnapshot.CurrentBranchName);
                    if (currentIndex < 0) currentIndex = 0;
                    _selectedBranchName = branchNames[currentIndex];
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    int newIndex = EditorGUILayout.Popup(currentIndex, branchNames);
                    if (newIndex != currentIndex)
                    {
                        _selectedBranchName = branchNames[newIndex];
                    }

                    if (GUILayout.Button("Switch", GUILayout.Width(60f)))
                    {
                        SwitchBranchAsync(_selectedBranchName);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No branches found or error loading branches.");
            }
        }

        private void DrawRemoteControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Remote", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Pull"))
                {
                    RunOperationBackground(() =>
                    {
                        var result = UnityGitRemoteService.PullCurrentBranch(_statusSnapshot.RepositoryRoot);
                        return (result.Success, result.ErrorMessage);
                    }, "Pull successful.");
                }
                if (GUILayout.Button("Push"))
                {
                    RunOperationBackground(() =>
                    {
                        var result = UnityGitRemoteService.PushCurrentBranch(_statusSnapshot.RepositoryRoot);
                        return (result.Success, result.ErrorMessage);
                    }, "Push successful.");
                }
            }
        }

        private void DrawHistoryPanel()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
                if (GUILayout.Button("Open Graph", GUILayout.Width(100f)))
                {
                    UnityGitHistoryGraphWindow.ShowWindow();
                }
            }

            if (_historySnapshot == null)
            {
                EditorGUILayout.LabelField("Loading history...");
                return;
            }

            if (!string.IsNullOrEmpty(_historySnapshot.ErrorMessage))
            {
                EditorGUILayout.HelpBox(_historySnapshot.ErrorMessage, MessageType.Warning);
                return;
            }

            if (_historySnapshot.Entries.Count == 0)
            {
                EditorGUILayout.LabelField(_historySnapshot.FriendlyMessage);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8f);
                using (new EditorGUILayout.VerticalScope(Styles.FileListBackground))
                {
                    bool isAlternate = false;
                    foreach (var entry in _historySnapshot.Entries)
                    {
                        DrawCommitEntry(entry, isAlternate);
                        isAlternate = !isAlternate;
                    }
                }
                GUILayout.Space(8f);
            }
        }

        private void DrawCommitEntry(GitCommitEntry entry, bool isAlternate)
        {
            var rect = EditorGUILayout.BeginHorizontal(Styles.FileEntry);

            if (Event.current.type == EventType.Repaint)
            {
                if (isAlternate)
                {
                    EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.05f));
                }
            }

            EditorGUILayout.LabelField(entry.ShortHash, Styles.CommitHash);
            EditorGUILayout.LabelField(entry.Subject, Styles.CommitSubject);
            EditorGUILayout.LabelField(entry.AuthorName, Styles.CommitAuthor);

            EditorGUILayout.EndHorizontal();
        }

        private static class Styles
        {
            public static GUIStyle SummaryBox;
            public static GUIStyle SummaryLabel;
            public static GUIStyle SummaryValue;
            public static GUIStyle FileListBackground;
            public static GUIStyle FileEntry;
            public static GUIStyle CommitHash;
            public static GUIStyle CommitSubject;
            public static GUIStyle CommitAuthor;
            public static GUIStyle StatusCode;
            public static GUIStyle FilePath;

            private static bool _initialized;
            private static bool _isProSkin;

            public static void Initialize()
            {
                if (_initialized && _isProSkin == EditorGUIUtility.isProSkin) return;

                _isProSkin = EditorGUIUtility.isProSkin;

                SummaryBox = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10),
                    margin = new RectOffset(0, 0, 8, 8)
                };

                SummaryLabel = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    fixedWidth = 100
                };

                SummaryValue = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true
                };

                FileListBackground = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(1, 1, 1, 1),
                    margin = new RectOffset(0, 0, 0, 0)
                };

                FileEntry = new GUIStyle()
                {
                    padding = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(0, 0, 0, 0)
                };

                CommitHash = new GUIStyle(EditorStyles.label)
                {
                    fixedWidth = 60,
                    fontStyle = FontStyle.Bold
                };

                CommitSubject = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = false
                };

                CommitAuthor = new GUIStyle(EditorStyles.miniLabel)
                {
                    fixedWidth = 100,
                    alignment = TextAnchor.MiddleRight
                };

                StatusCode = new GUIStyle(EditorStyles.label)
                {
                    fixedWidth = 24,
                    fontStyle = FontStyle.Bold
                };

                FilePath = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = false
                };

                _initialized = true;
            }
        }
    }
}
