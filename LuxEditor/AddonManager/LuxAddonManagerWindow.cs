using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Linalab.Lux.Editor
{
    public sealed class LuxAddonManagerWindow : EditorWindow
    {
        Vector2 _leftScrollPosition;
        Vector2 _rightScrollPosition;
        List<LuxAddonManifest> _availableAddons = new List<LuxAddonManifest>();
        LuxAddonManifest _selectedAddon;
        string _message = string.Empty;
        bool _isProcessing = false;
        string _progressMessage = string.Empty;

        [MenuItem("Window/Linalab/Lux Addon Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<LuxAddonManagerWindow>();
            window.titleContent = new GUIContent("Lux Addons");
            window.minSize = new Vector2(700f, 500f);
            window.Refresh();
            window.Show();
        }

        void OnEnable()
        {
            Refresh();
        }

        void OnGUI()
        {
            DrawHeader();

            if (_isProcessing)
            {
                DrawProgress();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPane();
                DrawRightPane();
            }

            DrawFooter();
        }

        void DrawHeader()
        {
            int installedCount = _availableAddons.Count(addon => LuxAddonManager.IsInstalled(addon.name));
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Lux Addon Manager", EditorStyles.toolbarButton, GUILayout.Width(150));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Installed: {installedCount}/{_availableAddons.Count}", EditorStyles.toolbarButton);
            }

            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
        }

        void DrawProgress()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField(_progressMessage, EditorStyles.boldLabel, GUILayout.Height(30));
            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(rect, 0.5f, "Processing...");
            Repaint();
        }

        void DrawLeftPane()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(250f)))
            {
                _leftScrollPosition = EditorGUILayout.BeginScrollView(_leftScrollPosition, "box");

                foreach (var addon in _availableAddons)
                {
                    DrawAddonListItem(addon);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawAddonListItem(LuxAddonManifest addon)
        {
            bool isSelected = _selectedAddon == addon;
            bool isInstalled = LuxAddonManager.IsInstalled(addon.name);
            
            GUIStyle style = new GUIStyle(EditorStyles.helpBox);
            if (isSelected)
            {
                style.normal.background = Texture2D.whiteTexture;
            }

            Color prevColor = GUI.backgroundColor;
            if (isSelected)
            {
                GUI.backgroundColor = new Color(0.2f, 0.4f, 0.8f, 0.5f);
            }

            using (new EditorGUILayout.VerticalScope(style))
            {
                GUI.backgroundColor = prevColor;

                if (GUILayout.Button(addon.DisplayTitle, EditorStyles.label))
                {
                    _selectedAddon = addon;
                    GUI.FocusControl(null);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (isInstalled)
                    {
                        GUI.color = InstalledColor;
                        GUILayout.Label("Installed", EditorStyles.miniLabel);
                        GUI.color = prevColor;
                    }
                    else
                    {
                        GUI.color = AvailableColor;
                        GUILayout.Label("Available", EditorStyles.miniLabel);
                        GUI.color = prevColor;
                    }

                    GUILayout.FlexibleSpace();

                    bool dependenciesValid = LuxAddonDependencyResolver.ValidateDependencies(addon, _availableAddons, out _);
                    
                    if (isInstalled)
                    {
                        if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60f)))
                        {
                            _selectedAddon = addon;
                            RemoveAddon(addon);
                        }
                    }
                    else
                    {
                        EditorGUI.BeginDisabledGroup(!dependenciesValid);
                        if (GUILayout.Button("Install", EditorStyles.miniButton, GUILayout.Width(60f)))
                        {
                            _selectedAddon = addon;
                            InstallAddon(addon);
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                }
            }
        }

        void DrawRightPane()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (_selectedAddon == null)
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        GUILayout.Label("Select an addon to view details", EditorStyles.centeredGreyMiniLabel);
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.FlexibleSpace();
                    return;
                }

                _rightScrollPosition = EditorGUILayout.BeginScrollView(_rightScrollPosition);

                bool isInstalled = LuxAddonManager.IsInstalled(_selectedAddon.name);
                bool dependenciesValid = LuxAddonDependencyResolver.ValidateDependencies(_selectedAddon, _availableAddons, out string[] errors);

                // Header
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(_selectedAddon.DisplayTitle, EditorStyles.largeLabel);
                    GUILayout.FlexibleSpace();
                    if (isInstalled)
                    {
                        GUI.color = InstalledColor;
                        GUILayout.Label("INSTALLED", EditorStyles.boldLabel);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        GUI.color = AvailableColor;
                        GUILayout.Label("AVAILABLE", EditorStyles.boldLabel);
                        GUI.color = Color.white;
                    }
                }

                EditorGUILayout.Space();

                // Action Buttons
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (isInstalled)
                    {
                        if (GUILayout.Button("Remove", GUILayout.Height(30)))
                        {
                            RemoveAddon(_selectedAddon);
                        }
                        if (GUILayout.Button("Reinstall (Update)", GUILayout.Height(30)))
                        {
                            ReinstallAddon(_selectedAddon);
                        }
                    }
                    else
                    {
                        EditorGUI.BeginDisabledGroup(!dependenciesValid);
                        if (GUILayout.Button("Install", GUILayout.Height(30)))
                        {
                            InstallAddon(_selectedAddon);
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                }

                if (!dependenciesValid)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Warning);
                }

                EditorGUILayout.Space();
                DrawDetailRow("Name", _selectedAddon.name);
                DrawDetailRow("Version", _selectedAddon.Version);
                DrawDetailRow("Category", _selectedAddon.Category);
                
                EditorGUILayout.Space();
                GUILayout.Label("Description", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(_selectedAddon.description) ? "No description provided." : _selectedAddon.description, MessageType.None);

                EditorGUILayout.Space();
                DrawListDetail("Define Symbols", _selectedAddon.DefineSymbols);
                
                EditorGUILayout.Space();
                DrawDictionaryDetail("Required Unity Packages", _selectedAddon.RequiredPackages);
                
                EditorGUILayout.Space();
                DrawDictionaryDetail("Addon Dependencies", _selectedAddon.AddonDependencies);
                
                EditorGUILayout.Space();
                DrawListDetail("Assemblies", _selectedAddon.Assemblies);

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawDetailRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.Width(150));
                GUILayout.Label(value);
            }
        }

        void DrawListDetail(string label, string[] items)
        {
            GUILayout.Label(label, EditorStyles.boldLabel);
            if (items == null || items.Length == 0)
            {
                GUILayout.Label("None", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var item in items)
                {
                    GUILayout.Label($"• {item}");
                }
            }
        }

        void DrawDictionaryDetail(string label, Dictionary<string, string> items)
        {
            GUILayout.Label(label, EditorStyles.boldLabel);
            if (items == null || items.Count == 0)
            {
                GUILayout.Label("None", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var kvp in items)
                {
                    GUILayout.Label($"• {kvp.Key} ({kvp.Value})");
                }
            }
        }

        void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                {
                    Refresh();
                }
            }
        }

        void InstallAddon(LuxAddonManifest addon)
        {
            _isProcessing = true;
            _progressMessage = $"Installing {addon.DisplayTitle}...";
            Repaint();

            EditorApplication.delayCall += () =>
            {
                try
                {
                    LuxAddonManager.Install(addon.name);
                    _message = $"Installed Lux addon '{addon.name}'.";
                }
                catch (Exception error)
                {
                    _message = error.Message;
                }
                finally
                {
                    _isProcessing = false;
                    Refresh();
                }
            };
        }

        void RemoveAddon(LuxAddonManifest addon)
        {
            string[] dependents = LuxAddonDependencyResolver.FindInstalledDependents(addon.name, LuxAddonManager.GetInstalledAddons());
            string warning = dependents.Length == 0
                ? $"Remove Lux addon '{addon.DisplayTitle}'?"
                : $"Remove Lux addon '{addon.DisplayTitle}'? Installed addons depend on it: {string.Join(", ", dependents)}";

            if (!EditorUtility.DisplayDialog("Remove Lux Addon", warning, "Remove", "Cancel"))
            {
                return;
            }

            _isProcessing = true;
            _progressMessage = $"Removing {addon.DisplayTitle}...";
            Repaint();

            EditorApplication.delayCall += () =>
            {
                try
                {
                    LuxAddonManager.Uninstall(addon.name);
                    _message = $"Removed Lux addon '{addon.name}'.";
                }
                catch (Exception error)
                {
                    _message = error.Message;
                }
                finally
                {
                    _isProcessing = false;
                    Refresh();
                }
            };
        }

        void ReinstallAddon(LuxAddonManifest addon)
        {
            _isProcessing = true;
            _progressMessage = $"Reinstalling {addon.DisplayTitle}...";
            Repaint();

            EditorApplication.delayCall += () =>
            {
                try
                {
                    LuxAddonManager.Uninstall(addon.name);
                    LuxAddonManager.Install(addon.name);
                    _message = $"Reinstalled Lux addon '{addon.name}'.";
                }
                catch (Exception error)
                {
                    _message = error.Message;
                }
                finally
                {
                    _isProcessing = false;
                    Refresh();
                }
            };
        }

        void Refresh()
        {
            _availableAddons = LuxAddonManager.DiscoverAvailableAddons()
                .OrderBy(addon => addon.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            if (_selectedAddon != null)
            {
                _selectedAddon = _availableAddons.FirstOrDefault(a => a.name == _selectedAddon.name);
            }

            Repaint();
        }

        static Color InstalledColor
        {
            get { return new Color(0.55f, 0.85f, 0.55f); }
        }

        static Color AvailableColor
        {
            get { return new Color(0.72f, 0.72f, 0.72f); }
        }

        static Color MissingDependencyColor
        {
            get { return new Color(1f, 0.85f, 0.35f); }
        }
    }
}
