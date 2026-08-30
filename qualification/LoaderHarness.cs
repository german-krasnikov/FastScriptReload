using System;
using System.IO;
using System.Reflection;
using FastScriptReload.Runtime.Polyfills;

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static string AssetPath;

        public static string GUIDToAssetPath(string guid)
        {
            return AssetPath;
        }
    }
}

namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public static string ResolvedPath;
        public string name;
        public string resolvedPath;

        public static PackageInfo FindForAssetPath(string assetPath)
        {
            return new PackageInfo
            {
                name = "com.handzlikchris.fastscriptreload",
                resolvedPath = ResolvedPath,
            };
        }
    }
}

internal static class LoaderHarness
{
    private const string ExpectedAssetPath =
        "Packages/com.handzlikchris.fastscriptreload/Plugins/Harmony/net48/0Harmony.dll.bytes";

    private static int Main(string[] args)
    {
        if (args.Length != 2) return 64;

        var mode = args[0];
        var assetsRoot = Path.GetFullPath(args[1]);
        UnityEditor.AssetDatabase.AssetPath = mode == "bad-path"
            ? ExpectedAssetPath + ".mutant"
            : ExpectedAssetPath;
        UnityEditor.PackageManager.PackageInfo.ResolvedPath = assetsRoot;

        var blob = Path.Combine(
            assetsRoot, "Plugins", "Harmony", "net48", "0Harmony.dll.bytes");
        if (mode == "preloaded") Assembly.Load(File.ReadAllBytes(blob));

        try
        {
            var first = Memory.GetHarmonyAssembly();
            var second = Memory.GetHarmonyAssembly();
            if (mode != "valid" || !ReferenceEquals(first, second)) return 65;
            if (first.GetName().Name != "0Harmony" ||
                first.GetName().Version != new Version(2, 4, 2, 0)) return 66;
            var embeddedCecilTypes = new[]
            {
                "Mono.Cecil.AssemblyDefinition",
                "Mono.Cecil.ReaderParameters",
                "Mono.Cecil.CustomAttribute",
                "Mono.Cecil.CustomAttributeArgument",
            };
            foreach (var typeName in embeddedCecilTypes)
            {
                if (first.GetType(typeName, false) == null) return 68;
            }
            Console.WriteLine("VALID");
            return 0;
        }
        catch (HarmonyDependencyException exception)
        {
            if (mode == "valid")
            {
                Console.Error.WriteLine(exception);
                return 67;
            }
            Console.WriteLine("REJECTED:" + mode);
            return 0;
        }
    }
}
