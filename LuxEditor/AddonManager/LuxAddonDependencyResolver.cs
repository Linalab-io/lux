using System;
using System.Collections.Generic;
using System.Linq;

namespace Linalab.Lux.Editor
{
    public static class LuxAddonDependencyResolver
    {
        public static List<LuxAddonManifest> ResolveInstallOrder(string addonName, IEnumerable<LuxAddonManifest> availableAddons)
        {
            var byName = BuildManifestMap(availableAddons);
            if (!byName.ContainsKey(addonName))
            {
                throw new InvalidOperationException($"Lux addon '{addonName}' was not found.");
            }

            var order = new List<LuxAddonManifest>();
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            VisitInstall(addonName, byName, visiting, visited, order);
            return order;
        }

        public static List<LuxAddonManifest> ResolveUninstallOrder(string addonName, IEnumerable<LuxAddonManifest> installedAddons)
        {
            var byName = BuildManifestMap(installedAddons);
            if (!byName.ContainsKey(addonName))
            {
                throw new InvalidOperationException($"Lux addon '{addonName}' is not installed.");
            }

            var order = new List<LuxAddonManifest>();
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            VisitUninstall(addonName, byName, visiting, visited, order);
            return order;
        }

        public static bool ValidateDependencies(LuxAddonManifest addon, IEnumerable<LuxAddonManifest> availableAddons, out string[] errors)
        {
            var result = new List<string>();
            var byName = BuildManifestMap(availableAddons);

            if (addon == null || string.IsNullOrEmpty(addon.name))
            {
                errors = new[] { "Addon manifest is missing a name." };
                return false;
            }

            foreach (string dependencyName in addon.AddonDependencies.Keys)
            {
                if (!byName.ContainsKey(dependencyName))
                {
                    result.Add($"Missing addon dependency '{dependencyName}'.");
                }
            }

            try
            {
                ResolveInstallOrder(addon.name, availableAddons);
            }
            catch (Exception error)
            {
                result.Add(error.Message);
            }

            errors = result.ToArray();
            return errors.Length == 0;
        }

        public static string[] FindInstalledDependents(string addonName, IEnumerable<LuxAddonManifest> installedAddons)
        {
            return installedAddons
                .Where(addon => addon != null && !string.Equals(addon.name, addonName, StringComparison.OrdinalIgnoreCase))
                .Where(addon => addon.AddonDependencies.Keys.Any(dependency => string.Equals(dependency, addonName, StringComparison.OrdinalIgnoreCase)))
                .Select(addon => addon.name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static void VisitInstall(
            string addonName,
            Dictionary<string, LuxAddonManifest> byName,
            HashSet<string> visiting,
            HashSet<string> visited,
            List<LuxAddonManifest> order)
        {
            if (visited.Contains(addonName))
            {
                return;
            }

            if (!byName.TryGetValue(addonName, out LuxAddonManifest addon))
            {
                throw new InvalidOperationException($"Missing addon dependency '{addonName}'.");
            }

            if (!visiting.Add(addonName))
            {
                throw new InvalidOperationException($"Circular Lux addon dependency detected at '{addonName}'.");
            }

            foreach (string dependencyName in addon.AddonDependencies.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                VisitInstall(dependencyName, byName, visiting, visited, order);
            }

            visiting.Remove(addonName);
            visited.Add(addonName);
            order.Add(addon);
        }

        static void VisitUninstall(
            string addonName,
            Dictionary<string, LuxAddonManifest> byName,
            HashSet<string> visiting,
            HashSet<string> visited,
            List<LuxAddonManifest> order)
        {
            if (visited.Contains(addonName))
            {
                return;
            }

            if (!byName.TryGetValue(addonName, out LuxAddonManifest addon))
            {
                throw new InvalidOperationException($"Missing installed addon '{addonName}'.");
            }

            if (!visiting.Add(addonName))
            {
                throw new InvalidOperationException($"Circular Lux addon dependency detected at '{addonName}'.");
            }

            foreach (LuxAddonManifest dependent in byName.Values
                .Where(candidate => candidate.AddonDependencies.Keys.Any(dependency => string.Equals(dependency, addonName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(candidate => candidate.name, StringComparer.OrdinalIgnoreCase))
            {
                VisitUninstall(dependent.name, byName, visiting, visited, order);
            }

            visiting.Remove(addonName);
            visited.Add(addonName);
            order.Add(addon);
        }

        static Dictionary<string, LuxAddonManifest> BuildManifestMap(IEnumerable<LuxAddonManifest> addons)
        {
            var byName = new Dictionary<string, LuxAddonManifest>(StringComparer.OrdinalIgnoreCase);
            if (addons == null)
            {
                return byName;
            }

            foreach (LuxAddonManifest addon in addons)
            {
                if (addon == null || string.IsNullOrEmpty(addon.name) || byName.ContainsKey(addon.name))
                {
                    continue;
                }

                byName.Add(addon.name, addon);
            }

            return byName;
        }
    }
}
