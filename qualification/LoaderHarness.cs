using System;
using System.IO;
using System.Reflection;
using System.Threading;
using FastScriptReload.Runtime.Polyfills;

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static string AssetPath;
        public static int LookupCount;

        public static string GUIDToAssetPath(string guid)
        {
            Interlocked.Increment(ref LookupCount);
            return AssetPath;
        }
    }
}

namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public static string ResolvedPath;
        public static int LookupCount;
        public string name;
        public string resolvedPath;

        public static PackageInfo FindForAssetPath(string assetPath)
        {
            Interlocked.Increment(ref LookupCount);
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
        "Packages/com.handzlikchris.fastscriptreload/Plugins/Harmony/Editor/0Harmony.dll.bytes";

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
            assetsRoot, "Plugins", "Harmony", "Editor", "0Harmony.dll.bytes");
        if (mode == "preloaded") Assembly.Load(File.ReadAllBytes(blob));

        try
        {
            if (mode == "concurrent") return RunConcurrent();
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

    private static int RunConcurrent()
    {
        const int count = 24;
        var gate = new ManualResetEvent(false);
        var threads = new Thread[count];
        var assemblies = new Assembly[count];
        var failures = new Exception[count];
        for (var i = 0; i < count; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                gate.WaitOne();
                try { assemblies[index] = Memory.GetHarmonyAssembly(); }
                catch (Exception error) { failures[index] = error; }
            });
            threads[i].Start();
        }
        gate.Set();
        foreach (var thread in threads) thread.Join();
        foreach (var failure in failures) if (failure != null) return 69;
        foreach (var assembly in assemblies)
            if (!ReferenceEquals(assemblies[0], assembly)) return 70;
        if (UnityEditor.AssetDatabase.LookupCount != 1 ||
            UnityEditor.PackageManager.PackageInfo.LookupCount != 1) return 71;
        Console.WriteLine("CONCURRENT");
        return 0;
    }
}
