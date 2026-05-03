using System;
using System.Collections.Generic;

namespace Linalab.Lux.Editor
{
    [Serializable]
    public sealed class LuxAddonManifest
    {
        public string name;
        public string displayName;
        public string version;
        public string description;
        public string category;
        public string[] defineSymbols = Array.Empty<string>();
        public Dictionary<string, string> requiredPackages = new Dictionary<string, string>();
        public Dictionary<string, string> addonDependencies = new Dictionary<string, string>();
        public string[] assemblies = Array.Empty<string>();
        public LuxAddonEndpoints endpoints = new LuxAddonEndpoints();
        public string[] keywords = Array.Empty<string>();

        public string DirectoryPath { get; set; }

        public string DisplayTitle
        {
            get
            {
                return string.IsNullOrEmpty(displayName) ? name : displayName;
            }
        }

        public string Category
        {
            get
            {
                return string.IsNullOrEmpty(category) ? "uncategorized" : category;
            }
        }

        public string Version
        {
            get
            {
                return string.IsNullOrEmpty(version) ? "0.0.0" : version;
            }
        }

        public string[] DefineSymbols
        {
            get
            {
                return defineSymbols ?? Array.Empty<string>();
            }
        }

        public Dictionary<string, string> RequiredPackages
        {
            get
            {
                return requiredPackages ?? new Dictionary<string, string>();
            }
        }

        public Dictionary<string, string> AddonDependencies
        {
            get
            {
                return addonDependencies ?? new Dictionary<string, string>();
            }
        }

        public string[] Assemblies
        {
            get
            {
                return assemblies ?? Array.Empty<string>();
            }
        }

        public string[] Keywords
        {
            get
            {
                return keywords ?? Array.Empty<string>();
            }
        }

        public LuxAddonEndpoints Endpoints
        {
            get
            {
                return endpoints ?? new LuxAddonEndpoints();
            }
        }

        [Serializable]
        public sealed class LuxAddonEndpoints
        {
            public string[] include = Array.Empty<string>();

            public string[] Include
            {
                get
                {
                    return include ?? Array.Empty<string>();
                }
            }
        }
    }
}
