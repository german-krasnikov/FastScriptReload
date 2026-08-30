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
        public HarmonyDependencyException(string message) : base(message)
        {
        }

        public HarmonyDependencyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public static class Memory
    {
#if UNITY_EDITOR
        private const string HarmonyBlobGuid = "494e757c92cba704db1d95279f80a30f";
        private const string HarmonyBlobSha256 = "77e6901ecc606aec66c2a972782a3779e4f50c037d2d165eb7ececdd4d8f794d";
        private const string HarmonyMvid = "B9E6CF65-9433-482B-8860-83CFF28D0128";
        private const string HarmonyAssemblyName = "0Harmony";
        private const string ProviderPackageName = "com.handzlikchris.fastscriptreload";
        private const string BlobAssetPath =
            "Packages/com.handzlikchris.fastscriptreload/Plugins/Harmony/net48/0Harmony.dll.bytes";

        private static readonly Version HarmonyVersion = new Version(2, 4, 2, 0);
        private static readonly object LoadGate = new object();
        private static Assembly _harmonyAssembly;
        private static MethodInfo _detourMethod;
        private static Exception _loadFailure;
        private static bool _loadAttempted;

        public static void DetourMethod(MethodBase original, MethodBase target)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (target == null) throw new ArgumentNullException(nameof(target));

            MethodInfo detourMethod;
            lock (LoadGate)
            {
                EnsureLoaded();
                detourMethod = _detourMethod;
            }

            try
            {
                detourMethod.Invoke(null, new object[] { original, target });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        internal static Assembly GetHarmonyAssembly()
        {
            lock (LoadGate)
            {
                EnsureLoaded();
                return _harmonyAssembly;
            }
        }

        private static void EnsureLoaded()
        {
            if (_detourMethod != null) return;
            if (_loadAttempted)
            {
                throw new HarmonyDependencyException(
                    "The pinned Harmony dependency failed its first load attempt.", _loadFailure);
            }

            _loadAttempted = true;
            try
            {
                LoadAndValidate();
            }
            catch (HarmonyDependencyException exception)
            {
                _loadFailure = exception;
                throw;
            }
            catch (Exception exception)
            {
                var failure = new HarmonyDependencyException(
                    "The pinned Harmony dependency could not be loaded.", exception);
                _loadFailure = failure;
                throw failure;
            }
        }

        private static void LoadAndValidate()
        {
            if (FindLoadedHarmonyAssemblies().Length != 0)
            {
                throw Failure("An unverified 0Harmony assembly is already loaded.");
            }

            var blobPath = ResolveBlobPath();
            var blobBytes = File.ReadAllBytes(blobPath);
            if (!String.Equals(ComputeSha256(blobBytes), HarmonyBlobSha256, StringComparison.Ordinal))
            {
                throw Failure("The Harmony blob SHA-256 does not match the frozen dependency.");
            }

            var loadedAssembly = Assembly.Load(blobBytes);
            var loadedHarmonyAssemblies = FindLoadedHarmonyAssemblies();
            if (loadedHarmonyAssemblies.Length != 1 ||
                !ReferenceEquals(loadedHarmonyAssemblies[0], loadedAssembly))
            {
                throw Failure("Harmony load did not produce exactly one verified assembly.");
            }

            ValidateAssemblyIdentity(loadedAssembly);
            var patchTools = loadedAssembly.GetType("HarmonyLib.PatchTools", false);
            if (patchTools == null)
            {
                throw Failure("HarmonyLib.PatchTools is missing from the pinned assembly.");
            }

            var candidates = patchTools
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsExactDetourMethod)
                .ToArray();
            if (candidates.Length != 1)
            {
                throw Failure("The exact PatchTools.DetourMethod contract is missing or ambiguous.");
            }

            _harmonyAssembly = loadedAssembly;
            _detourMethod = candidates[0];
        }

        private static string ResolveBlobPath()
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(HarmonyBlobGuid);
            if (!String.Equals(assetPath, BlobAssetPath, StringComparison.Ordinal))
            {
                throw Failure("The Harmony blob GUID does not resolve to its frozen package path.");
            }

            var packageInfo = PackageInfo.FindForAssetPath(assetPath);
            if (packageInfo == null ||
                !String.Equals(packageInfo.name, ProviderPackageName, StringComparison.Ordinal) ||
                String.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                throw Failure("The Harmony blob is not owned by the expected provider package.");
            }

            var packageRoot = Path.GetFullPath(packageInfo.resolvedPath);
            var relativePath = assetPath.Substring(("Packages/" + ProviderPackageName + "/").Length);
            var blobPath = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var rootPrefix = packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
            if (!blobPath.StartsWith(rootPrefix, comparison) || !File.Exists(blobPath))
            {
                throw Failure("The Harmony blob resolves outside the provider package or is missing.");
            }

            return blobPath;
        }

        private static Assembly[] FindLoadedHarmonyAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => String.Equals(
                    assembly.GetName().Name, HarmonyAssemblyName, StringComparison.Ordinal))
                .ToArray();
        }

        private static void ValidateAssemblyIdentity(Assembly assembly)
        {
            var name = assembly.GetName();
            var publicKeyToken = name.GetPublicKeyToken();
            if (!String.Equals(name.Name, HarmonyAssemblyName, StringComparison.Ordinal) ||
                name.Version != HarmonyVersion ||
                !String.IsNullOrEmpty(name.CultureName) ||
                (publicKeyToken != null && publicKeyToken.Length != 0) ||
                assembly.ManifestModule.ModuleVersionId != new Guid(HarmonyMvid))
            {
                throw Failure("The loaded Harmony assembly identity or MVID is not frozen.");
            }
        }

        private static bool IsExactDetourMethod(MethodInfo method)
        {
            if (!String.Equals(method.Name, "DetourMethod", StringComparison.Ordinal) ||
                !method.IsStatic || method.ReturnType != typeof(void))
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 2 &&
                   parameters[0].ParameterType == typeof(MethodBase) &&
                   parameters[1].ParameterType == typeof(MethodBase);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(bytes))
                    .Replace("-", String.Empty)
                    .ToLowerInvariant();
            }
        }

        private static HarmonyDependencyException Failure(string message)
        {
            return new HarmonyDependencyException(message);
        }
#else
        public static void DetourMethod(MethodBase original, MethodBase target)
        {
            throw new PlatformNotSupportedException(
                "The pinned Fast Script Reload detour is supported only in the Unity Editor.");
        }

        internal static Assembly GetHarmonyAssembly()
        {
            throw new PlatformNotSupportedException(
                "The pinned Fast Script Reload dependency is supported only in the Unity Editor.");
        }
#endif
    }
}
