using System.IO;
using Linalab.UnityAiBridge.Editor;
using Linalab.UnityGit.Editor;
using UnityEditor;
using UnityEngine;

namespace Linalab.Lux.Editor
{
    public sealed class LuxWorkbenchWindow : EditorWindow
    {
        static readonly LuxAutomationGateway AutomationGateway = new LuxAutomationGateway();

        Vector2 _scrollPosition;
        string _sampleCommand = "git status --short";
        bool _approvalGranted;
        string _lastAutomationMessage = string.Empty;
        string _lastRustCliMessage = string.Empty;
        bool _isInstallingRustCli;

        [MenuItem("Window/Linalab/Lux Workbench")]
        public static void ShowWindow()
        {
            var window = GetWindow<LuxWorkbenchWindow>();
            window.titleContent = new GUIContent("Lux", EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image);
            window.minSize = new Vector2(460f, 360f);
            window.Show();
        }

        void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();
            DrawModuleLaunchers();
            DrawGitSummary();
            DrawAiBridgeControls();
            DrawRustCliControls();
            DrawUnityBridgeControls();
            DrawAutomationControls();
            DrawRemotePlan();

            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.LabelField("Lux Workbench", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Unified Phase 1 adapter for external terminals, AI clients, Git, AI Bridge, and automation guardrails.", MessageType.Info);
        }

        void DrawModuleLaunchers()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Git"))
                {
                    UnityGitWindow.ShowWindow();
                }

                if (GUILayout.Button("Export AI Context"))
                {
                    global::Linalab.UnityAiBridge.Editor.UnityAiBridge.ExportDefaultContext();
                }
            }
        }

        void DrawGitSummary()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Git Summary", EditorStyles.boldLabel);

            var status = UnityGitStatusService.ReadStatus(GetProjectRoot());
            if (!status.IsRepository)
            {
                EditorGUILayout.HelpBox(status.ErrorMessage, MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Repository", status.RepositoryRoot);
            EditorGUILayout.LabelField("Branch", status.BranchName);
            EditorGUILayout.LabelField("Changed Paths", status.Entries.Count.ToString());
        }

        void DrawAiBridgeControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("External Adapter", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Start Server"))
                {
                    UnityAiBridgeMenu.StartContextServer();
                }

                if (GUILayout.Button("Stop Server"))
                {
                    UnityAiBridgeMenu.StopContextServer();
                }

                if (GUILayout.Button("Copy MCP Command"))
                {
                    UnityAiBridgeMenu.CopyMcpHelperCommand();
                }
            }

            EditorGUILayout.LabelField("Discovery", UnityAiBridgeMenu.GetServerDiscoveryPath());
        }

        void DrawRustCliControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lux Rust CLI", EditorStyles.boldLabel);

            var status = LuxRustCliInstaller.GetStatus();
            EditorGUILayout.LabelField("cargo", status.CargoAvailable ? status.CargoPath : "Not found");
            EditorGUILayout.LabelField("lux", status.CliInstalled ? $"v{status.CliVersion}" : "Not installed");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_isInstallingRustCli);
                if (GUILayout.Button(status.CliInstalled ? "Update Global Rust CLI" : "Install Global Rust CLI"))
                {
                    InstallOrUpdateRustCli();
                }

                if (GUILayout.Button("Copy Terminal Command"))
                {
                    LuxRustCliInstaller.CopyInstallCommand();
                    _lastRustCliMessage = "Copied terminal install/update command.";
                }
                EditorGUI.EndDisabledGroup();
            }

            if (!string.IsNullOrEmpty(_lastRustCliMessage))
            {
                EditorGUILayout.HelpBox(_lastRustCliMessage, MessageType.None);
            }
        }

        async void InstallOrUpdateRustCli()
        {
            _isInstallingRustCli = true;
            _lastRustCliMessage = "Installing or updating Lux Rust CLI...";
            Repaint();

            var result = await LuxRustCliInstaller.InstallOrUpdateAsync();
            _isInstallingRustCli = false;
            _lastRustCliMessage = result.Message;
            Repaint();
        }

        void DrawUnityBridgeControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lux Unity Bridge", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Protocol", LuxBridgeSettings.Protocol);
            EditorGUILayout.LabelField("Settings", LuxBridgeSettings.SettingsRelativePath);

            if (GUILayout.Button("Write Lux Bridge Settings"))
            {
                var path = LuxBridgeSettings.WriteProjectSettings();
                EditorGUIUtility.systemCopyBuffer = path;
                _lastRustCliMessage = $"Wrote Lux bridge settings and copied path: {path}";
            }
        }

        void DrawAutomationControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Automation Safety", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Broad automation is enabled, but blocked and approval-required commands are audited.", MessageType.Warning);

            _sampleCommand = EditorGUILayout.TextField("Command", _sampleCommand);
            _approvalGranted = EditorGUILayout.Toggle("Approval Granted", _approvalGranted);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Evaluate"))
                {
                    var decision = AutomationGateway.Policy.Evaluate(_sampleCommand);
                    _lastAutomationMessage = $"{decision.Kind}: {decision.Reason}";
                }

                if (GUILayout.Button("Run Shell"))
                {
                    var result = AutomationGateway.ExecuteShellCommand(_sampleCommand, GetProjectRoot(), approvalGranted: _approvalGranted);
                    _lastAutomationMessage = result.Message;
                }
            }

            if (!string.IsNullOrEmpty(_lastAutomationMessage))
            {
                EditorGUILayout.HelpBox(_lastAutomationMessage, MessageType.None);
            }

            EditorGUILayout.LabelField("Audit Entries", AutomationGateway.AuditLog.Entries.Count.ToString());
        }

        void DrawRemotePlan()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Remote Gateway Plan", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Phase", LuxRemoteGatewayPlan.Phase);
            EditorGUILayout.LabelField("Video", LuxRemoteGatewayPlan.VideoTransport);
            EditorGUILayout.LabelField("Signaling", LuxRemoteGatewayPlan.SignalingTransport);
            EditorGUILayout.LabelField("Control", LuxRemoteGatewayPlan.ControlTransport);
            EditorGUILayout.LabelField("Permission", LuxRemoteGatewayPlan.PermissionModel);
            EditorGUILayout.LabelField("iOS Client Included", LuxRemoteGatewayPlan.IncludesIosClientImplementation ? "Yes" : "No");
        }

        static string GetProjectRoot()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? Directory.GetCurrentDirectory() : assetsDirectory.Parent.FullName;
        }
    }
}
