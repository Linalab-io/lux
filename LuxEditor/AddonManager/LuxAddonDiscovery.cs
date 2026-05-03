using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Linalab.Lux.Editor
{
    public static class LuxAddonDiscovery
    {
        const string ManifestFileName = "addon.json";

        public static List<LuxAddonManifest> DiscoverAddons()
        {
            var manifests = new List<LuxAddonManifest>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in GetDiscoveryRoots())
            {
                ScanRoot(root, manifests, seenNames);
            }

            return manifests;
        }

        public static string[] GetDiscoveryRoots()
        {
            string packageRoot = GetPackageRoot();
            string projectRoot = GetProjectRoot();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return new[]
            {
                Path.Combine(packageRoot, "Addons"),
                Path.Combine(projectRoot, ".lux", "addons"),
                Path.Combine(home, ".lux", "addons")
            };
        }

        static void ScanRoot(string root, List<LuxAddonManifest> manifests, HashSet<string> seenNames)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }

            string[] manifestPaths;
            try
            {
                manifestPaths = Directory.GetFiles(root, ManifestFileName, SearchOption.AllDirectories);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Failed to scan Lux addon directory '{root}': {error.Message}");
                return;
            }

            Array.Sort(manifestPaths, StringComparer.OrdinalIgnoreCase);
            foreach (string manifestPath in manifestPaths)
            {
                LuxAddonManifest manifest = ReadManifest(manifestPath);
                if (manifest == null)
                {
                    continue;
                }

                if (seenNames.Contains(manifest.name))
                {
                    continue;
                }

                seenNames.Add(manifest.name);
                manifests.Add(manifest);
            }
        }

        static LuxAddonManifest ReadManifest(string manifestPath)
        {
            try
            {
                var manifest = JsonConvert.DeserializeObject<LuxAddonManifest>(File.ReadAllText(manifestPath));
                if (manifest == null || string.IsNullOrEmpty(manifest.name))
                {
                    Debug.LogWarning($"Invalid Lux addon manifest without a name: {manifestPath}");
                    return null;
                }

                manifest.DirectoryPath = Path.GetDirectoryName(manifestPath);
                return manifest;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Failed to read Lux addon manifest '{manifestPath}': {error.Message}");
                return null;
            }
        }

        static string GetPackageRoot()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(LuxAddonDiscovery).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                return packageInfo.resolvedPath;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.linalab.lux"));
        }

        static string GetProjectRoot()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? Directory.GetCurrentDirectory() : assetsDirectory.Parent.FullName;
        }
    }
}
