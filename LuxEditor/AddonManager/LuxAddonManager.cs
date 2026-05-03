using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Linalab.Lux.Editor
{
    public static class LuxAddonManager
    {
        const int StateVersion = 1;
        const string StateRelativePath = "Library/Lux/addon-state.json";

        static readonly string[] DefaultInstalledAddons =
        {
            "webrtc",
            "codex-image",
            "pipeline-editor",
            "unity-git",
            "multi-ai"
        };

        static LuxAddonState _state;
        static List<LuxAddonManifest> _availableAddons;

        [InitializeOnLoadMethod]
        public static void Initialize()
        {
            LoadState();
        }

        public static List<LuxAddonManifest> DiscoverAvailableAddons()
        {
            _availableAddons = LuxAddonDiscovery.DiscoverAddons();
            return new List<LuxAddonManifest>(_availableAddons);
        }

        public static List<LuxAddonManifest> GetInstalledAddons()
        {
            LuxAddonState state = LoadState();
            var installedNames = new HashSet<string>(state.installedAddons ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return GetAvailableAddons()
                .Where(addon => installedNames.Contains(addon.name))
                .OrderBy(addon => addon.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsInstalled(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            LuxAddonState state = LoadState();
            return (state.installedAddons ?? Array.Empty<string>()).Any(addon => string.Equals(addon, name, StringComparison.OrdinalIgnoreCase));
        }

        public static void Install(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Addon name is required.", nameof(name));
            }

            var available = GetAvailableAddons();
            var order = LuxAddonDependencyResolver.ResolveInstallOrder(name, available);
            LuxAddonState state = LoadState();
            var installed = new HashSet<string>(state.installedAddons ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var symbols = new List<string>();

            foreach (LuxAddonManifest addon in order)
            {
                installed.Add(addon.name);
                symbols.AddRange(addon.DefineSymbols);
            }

            state.installedAddons = installed.OrderBy(addon => addon, StringComparer.OrdinalIgnoreCase).ToArray();
            state.lastUpdated = DateTime.UtcNow.ToString("o");
            SaveState(state);
            AddDefineSymbols(symbols.ToArray());
        }

        public static void Uninstall(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Addon name is required.", nameof(name));
            }

            var installedAddons = GetInstalledAddons();
            string[] dependents = LuxAddonDependencyResolver.FindInstalledDependents(name, installedAddons);
            if (dependents.Length > 0)
            {
                throw new InvalidOperationException($"Cannot remove Lux addon '{name}' because installed addons depend on it: {string.Join(", ", dependents)}");
            }

            var order = LuxAddonDependencyResolver.ResolveUninstallOrder(name, installedAddons);
            LuxAddonState state = LoadState();
            var installed = new HashSet<string>(state.installedAddons ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var symbols = new List<string>();

            foreach (LuxAddonManifest addon in order)
            {
                installed.Remove(addon.name);
                symbols.AddRange(addon.DefineSymbols);
            }

            state.installedAddons = installed.OrderBy(addon => addon, StringComparer.OrdinalIgnoreCase).ToArray();
            state.lastUpdated = DateTime.UtcNow.ToString("o");
            SaveState(state);
            RemoveDefineSymbols(symbols.ToArray());
        }

        public static void AddDefineSymbols(string[] defineSymbols)
        {
            UpdateDefineSymbols(defineSymbols, add: true);
        }

        public static void RemoveDefineSymbols(string[] defineSymbols)
        {
            UpdateDefineSymbols(defineSymbols, add: false);
        }

        public static string GetStatePath()
        {
            return Path.Combine(GetProjectRoot(), StateRelativePath);
        }

        static List<LuxAddonManifest> GetAvailableAddons()
        {
            if (_availableAddons == null)
            {
                DiscoverAvailableAddons();
            }

            return _availableAddons ?? new List<LuxAddonManifest>();
        }

        static LuxAddonState LoadState()
        {
            if (_state != null)
            {
                return _state;
            }

            string statePath = GetStatePath();
            if (!File.Exists(statePath))
            {
                _state = CreateDefaultState();
                SaveState(_state);
                return _state;
            }

            try
            {
                _state = JsonConvert.DeserializeObject<LuxAddonState>(File.ReadAllText(statePath)) ?? CreateDefaultState();
                if (_state.installedAddons == null)
                {
                    _state.installedAddons = Array.Empty<string>();
                }

                return _state;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Failed to read Lux addon state '{statePath}': {error.Message}");
                _state = CreateDefaultState();
                SaveState(_state);
                return _state;
            }
        }

        static void SaveState(LuxAddonState state)
        {
            string statePath = GetStatePath();
            Directory.CreateDirectory(Path.GetDirectoryName(statePath));
            state.version = StateVersion;
            File.WriteAllText(statePath, JsonConvert.SerializeObject(state, Formatting.Indented));
            _state = state;
        }

        static LuxAddonState CreateDefaultState()
        {
            return new LuxAddonState
            {
                version = StateVersion,
                installedAddons = DefaultInstalledAddons.ToArray(),
                lastUpdated = DateTime.UtcNow.ToString("o")
            };
        }

        static void UpdateDefineSymbols(string[] defineSymbols, bool add)
        {
            var normalized = (defineSymbols ?? Array.Empty<string>())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (normalized.Length == 0)
            {
                return;
            }

            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            string existingSymbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var symbols = new HashSet<string>(
                existingSymbols.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(symbol => symbol.Trim()),
                StringComparer.Ordinal);

            foreach (string symbol in normalized)
            {
                if (add)
                {
                    symbols.Add(symbol);
                }
                else
                {
                    symbols.Remove(symbol);
                }
            }

            string updatedSymbols = string.Join(";", symbols.OrderBy(symbol => symbol, StringComparer.Ordinal));
            if (!string.Equals(existingSymbols, updatedSymbols, StringComparison.Ordinal))
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, updatedSymbols);
            }
        }

        static string GetProjectRoot()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? Directory.GetCurrentDirectory() : assetsDirectory.Parent.FullName;
        }

        [Serializable]
        sealed class LuxAddonState
        {
            public int version;
            public string[] installedAddons = Array.Empty<string>();
            public string lastUpdated;
        }
    }
}
