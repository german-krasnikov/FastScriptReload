using System;
using System.Reflection;
using System.Runtime.CompilerServices;
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#endif

[assembly: InternalsVisibleTo("FastScriptReload.Editor")]

namespace FastScriptReload.Runtime.Polyfills
{
    public sealed class HarmonyDependencyException : InvalidOperationException
    {
        public HarmonyDependencyException(string message) : base(message) { }
        public HarmonyDependencyException(string message, Exception inner) : base(message, inner) { }
    }

    public static class Memory
    {
#if UNITY_EDITOR
        const string HarmonyBlobGuid = "494e757c92cba704db1d95279f80a30f";
        const string HarmonyBlobSha256 = "77e6901ecc606aec66c2a972782a3779e4f50c037d2d165eb7ececdd4d8f794d";
        const string HarmonyMvid = "B9E6CF65-9433-482B-8860-83CFF28D0128";
        const string PackageName = "com.handzlikchris.fastscriptreload";
        const string BlobAssetPath = "Packages/com.handzlikchris.fastscriptreload/Plugins/Harmony/net48/0Harmony.dll.bytes";

        sealed class State
        {
            internal readonly Assembly Assembly;
            internal readonly MethodInfo Detour;
            internal State(Assembly assembly, MethodInfo detour) { Assembly = assembly; Detour = detour; }
        }

        static readonly Lazy<State> Verified = new Lazy<State>(Load);

        public static void DetourMethod(MethodBase original, MethodBase target)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (target == null) throw new ArgumentNullException(nameof(target));
            try { Verified.Value.Detour.Invoke(null, new object[] { original, target }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        internal static Assembly GetHarmonyAssembly() { return Verified.Value.Assembly; }

        static State Load()
        {
            try { return LoadVerified(); }
            catch (HarmonyDependencyException) { throw; }
            catch (Exception error) { throw new HarmonyDependencyException("Pinned Harmony load failed.", error); }
        }

        static State LoadVerified()
        {
            if (Loaded().Length != 0) throw Failure("An unverified 0Harmony assembly is already loaded.");
            var assetPath = AssetDatabase.GUIDToAssetPath(HarmonyBlobGuid);
            var package = PackageInfo.FindForAssetPath(assetPath);
            if (assetPath != BlobAssetPath || package == null || package.name != PackageName || String.IsNullOrEmpty(package.resolvedPath))
                throw Failure("Harmony blob ownership or asset path is not frozen.");

            var root = Path.GetFullPath(package.resolvedPath);
            var relative = BlobAssetPath.Substring(("Packages/" + PackageName + "/").Length);
            var blobPath = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!blobPath.StartsWith(prefix, comparison) || !File.Exists(blobPath)) throw Failure("Harmony blob escaped its package root.");

            var blobBytes = File.ReadAllBytes(blobPath);
            if (Sha256(blobBytes) != HarmonyBlobSha256) throw Failure("Harmony blob SHA-256 is not frozen.");
            var assembly = Assembly.Load(blobBytes);
            var loaded = Loaded();
            if (loaded.Length != 1 || !ReferenceEquals(loaded[0], assembly)) throw Failure("Harmony load is ambiguous.");

            var name = assembly.GetName();
            var token = name.GetPublicKeyToken();
            if (name.Name != "0Harmony" || name.Version != new Version(2, 4, 2, 0) ||
                !String.IsNullOrEmpty(name.CultureName) || (token != null && token.Length != 0) ||
                assembly.ManifestModule.ModuleVersionId != new Guid(HarmonyMvid)) throw Failure("Harmony identity is not frozen.");

            var type = assembly.GetType("HarmonyLib.PatchTools", false);
            var methods = type == null ? new MethodInfo[0] : type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(IsDetour).ToArray();
            if (methods.Length != 1) throw Failure("Exact PatchTools.DetourMethod ABI is missing or ambiguous.");
            return new State(assembly, methods[0]);
        }

        static bool IsDetour(MethodInfo method)
        {
            var parameters = method.GetParameters();
            return method.Name == "DetourMethod" && method.IsStatic && method.ReturnType == typeof(void) && parameters.Length == 2 &&
                   parameters[0].ParameterType == typeof(MethodBase) && parameters[1].ParameterType == typeof(MethodBase);
        }

        static Assembly[] Loaded() { return AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "0Harmony").ToArray(); }
        static HarmonyDependencyException Failure(string message) { return new HarmonyDependencyException(message); }
        static string Sha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
#else
        public static void DetourMethod(MethodBase original, MethodBase target) { throw new PlatformNotSupportedException(); }
        internal static Assembly GetHarmonyAssembly() { throw new PlatformNotSupportedException(); }
#endif
    }
}
