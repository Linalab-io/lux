using System.IO;
using Linalab.UnityAiBridge.Editor;
#if LUX_UNITY_GIT
using Linalab.UnityGit.Editor;
#endif
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
            DrawEnvironmentStatus();
            DrawModuleLaunchers();
            DrawGitSummary();
            DrawAiBridgeControls();
            DrawRustCliControls();
            DrawUnityBridgeControls();
            DrawAutomationControls();
            DrawRemoteAddonStatus();

            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.LabelField("Lux Workbench", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Unified Phase 1 adapter for external terminals, AI clients, Git, AI Bridge, and automation guardrails.", MessageType.Info);
        }

        void DrawEnvironmentStatus()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Environment Status", EditorStyles.boldLabel);

            var cliStatus = LuxRustCliInstaller.GetStatus();
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect rect = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(20f));
                Color cliColor = cliStatus.CliInstalled ? new Color(0.2f, 0.75f, 0.25f) : (cliStatus.CargoAvailable ? new Color(0.95f, 0.7f, 0.15f) : new Color(0.85f, 0.2f, 0.2f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, 14f, 14f), cliColor);
                
                string cliLabel = cliStatus.CliInstalled ? $"Rust CLI Installed (v{cliStatus.CliVersion})" : (cliStatus.CargoAvailable ? "Rust CLI Missing (Cargo Available)" : "Rust CLI & Cargo Missing");
                EditorGUILayout.LabelField(cliLabel, EditorStyles.boldLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect rect = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(20f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, 14f, 14f), LuxServerStatusIndicator.StatusColor(LuxServerStatusIndicator.CurrentStatus));
                
                string serverLabel = $"Rust Gateway HTTP: {LuxServerStatusIndicator.StatusLabel(LuxServerStatusIndicator.CurrentStatus)}";
                if (LuxServerStatusIndicator.CurrentStatus == LuxServerStatusIndicator.LuxServerStatus.Alive)
                {
                    serverLabel += $" (Uptime: {LuxServerStatusIndicator.FormatUptime(LuxServerStatusIndicator.UptimeSeconds)})";
                }
                EditorGUILayout.LabelField(serverLabel, EditorStyles.boldLabel);

                if (GUILayout.Button("Check", GUILayout.Width(60f)))
                {
                    LuxServerStatusIndicator.ForceCheck();
                }
            }
            
            if (LuxServerStatusIndicator.CurrentStatus != LuxServerStatusIndicator.LuxServerStatus.Alive && LuxServerStatusIndicator.CurrentStatus != LuxServerStatusIndicator.LuxServerStatus.Unknown)
            {
                EditorGUILayout.HelpBox(LuxServerStatusIndicator.CurrentMessage + " This does not block `lux unity ...` TCP commands or dynamic code execution.", MessageType.Warning);
            }
        }

        void DrawModuleLaunchers()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
#if LUX_UNITY_GIT
                if (GUILayout.Button("Open Git"))
                {
                    UnityGitWindow.ShowWindow();
                }
#else
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("Open Git (Addon Not Installed)");
                EditorGUI.EndDisabledGroup();
#endif

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

#if LUX_UNITY_GIT
            var status = UnityGitStatusService.ReadStatus(GetProjectRoot());
            if (!status.IsRepository)
            {
                EditorGUILayout.HelpBox(status.ErrorMessage, MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Repository", status.RepositoryRoot);
            EditorGUILayout.LabelField("Branch", status.BranchName);
            EditorGUILayout.LabelField("Changed Paths", status.Entries.Count.ToString());
#else
            EditorGUILayout.HelpBox("Install the Unity Git addon to enable Git status in the workbench.", MessageType.Info);
#endif
        }

        void DrawAiBridgeControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Unity AI Bridge TCP Adapter", EditorStyles.boldLabel);

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

            var bridgeRunning = UnityAiBridgeMenu.IsContextServerRunning();
            EditorGUILayout.LabelField("Discovery", UnityAiBridgeMenu.GetServerDiscoveryPath());

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Status", GUILayout.Width(EditorGUIUtility.labelWidth));
                Rect rect = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(16f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, 12f, 12f), bridgeRunning ? new Color(0.2f, 0.75f, 0.25f) : new Color(0.95f, 0.7f, 0.15f));
                EditorGUILayout.LabelField(bridgeRunning ? "Running (focus independent)" : "Stopped");
            }

            EditorGUILayout.HelpBox("Rust CLI Unity commands and dynamic-code execution use this TCP adapter on Unity's main thread; they do not require the Unity Editor window to be focused.", MessageType.Info);
        }

        void DrawRustCliControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lux Rust CLI", EditorStyles.boldLabel);

            var status = LuxRustCliInstaller.GetStatus();
            EditorGUILayout.LabelField("cargo", status.CargoAvailable ? status.CargoPath : "Not found");
            EditorGUILayout.LabelField("lux", status.CliInstalled ? $"v{status.CliVersion}" : "Not installed");

            if (!status.CargoAvailable)
            {
                EditorGUILayout.HelpBox("Cargo is not available. Please install Rust from https://rustup.rs to enable the Lux CLI.", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_isInstallingRustCli || !status.CargoAvailable);
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

        void DrawRemoteAddonStatus()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Remote Gateway", EditorStyles.boldLabel);
#if LUX_WEBRTC
            EditorGUILayout.HelpBox("WebRTC remote streaming addon is enabled.", MessageType.Info);
#else
            EditorGUILayout.HelpBox("Install the WebRTC addon to enable remote streaming.", MessageType.None);
#endif
        }

        static string GetProjectRoot()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? Directory.GetCurrentDirectory() : assetsDirectory.Parent.FullName;
        }
    }
}
