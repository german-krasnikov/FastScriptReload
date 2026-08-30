using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FastScriptReload.Editor.Compilation;
using FastScriptReload.Runtime;
using ImmersiveVRTools.Runtime.Common;
using UnityEngine;
using UnityMCP.Editor.SourcePatch;

namespace Biome.SourcePatch.FSRAdapter
{
    /// <summary>
    /// The one adapter (Plans/HotReload/V2/FSR-MVP-CLEAN/
    /// 04-PARETO-COMPLETION-HANDOFF.md SS6 P0-60): the only class in this
    /// package with engine knowledge. Owns only body admission, replacement
    /// compilation and exact detour application -- the coordinator (main
    /// UnityMCP.Editor.SourcePatch module) owns source bytes, CAS/readback,
    /// the AutoRefresh lease, state and recovery. Registered exactly once by
    /// BiomeFsrAutomaticModesGuard.
    /// </summary>
    internal sealed class BiomeFsrSourcePatchProvider : ISourcePatchProvider
    {
        private readonly BiomeSingleFlightGate _gate = new BiomeSingleFlightGate();
        private static UnityMainThreadDispatcher _dispatcher;

        public SourcePatchApplyOutcome Apply(SourcePatchRequest request)
        {
            // The coordinator already guarantees one in-flight source
            // transaction at a time (SS1.1/SS3.1); this closes the gap
            // defensively on the engine side if that contract is ever
            // violated by the host: a nested/concurrent Apply call must
            // never reach Compile.
            if (!_gate.TryEnter())
            {
                return SourcePatchApplyOutcome.Uncertain;
            }

            try
            {
                return ApplyLocked(request);
            }
            finally
            {
                _gate.Exit();
            }
        }

        private SourcePatchApplyOutcome ApplyLocked(SourcePatchRequest request)
        {
            var beforeText = Encoding.UTF8.GetString(request.ExpectedBeforeContent);
            var afterText = Encoding.UTF8.GetString(request.NewContent);
            var classification = BiomeBodyOnlyMethodClassifier.Classify(beforeText, afterText);
            if (classification.Classification != BodyOnlyClassification.Admitted)
            {
                return SourcePatchApplyOutcome.Rejected;
            }

            if (!ProjectTypeCache.AllTypesInNonDynamicGeneratedAssemblies.TryGetValue(
                    classification.DeclaringTypeFullName, out var existingType))
            {
                return SourcePatchApplyOutcome.Rejected;
            }

            var candidateMethods = existingType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == classification.MethodName)
                .ToList();
            if (candidateMethods.Count != 1)
            {
                // Zero matches (stale cache) or more than one overload
                // sharing this name: this MVP classifier does not
                // disambiguate overloads by parameter list, so fail closed
                // rather than guess which one changed.
                return SourcePatchApplyOutcome.Rejected;
            }

            var existingMethod = candidateMethods[0];

            CompileResult compileResult;
            try
            {
                var absolutePath = ResolveAbsolutePath(request.AssetPath);
                compileResult = DynamicAssemblyCompiler.Compile(
                    new List<string> { absolutePath }, GetOrCreateDispatcher());
            }
            catch (Exception)
            {
                // Nothing was ever loaded/applied: compilation itself never
                // produced a dynamic assembly.
                return SourcePatchApplyOutcome.Rejected;
            }

            if (compileResult == null || compileResult.IsError)
            {
                return SourcePatchApplyOutcome.Rejected;
            }

            ExactTargetPatchResult patchResult;
            try
            {
                patchResult = AssemblyChangesLoader.Instance.DynamicallyUpdateSingleMethodForCreatedAssembly(
                    compileResult.CompiledAssembly, existingMethod);
            }
            catch (Exception)
            {
                // A dynamic assembly was already loaded into this AppDomain
                // by the compile step above: that side effect cannot be
                // cleanly undone, so this can never be reported as a clean
                // Rejected -- only Uncertain, never retried.
                return SourcePatchApplyOutcome.Uncertain;
            }

            return patchResult.Applied ? SourcePatchApplyOutcome.Applied : SourcePatchApplyOutcome.Uncertain;
        }

        private static string ResolveAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static UnityMainThreadDispatcher GetOrCreateDispatcher()
        {
            if (_dispatcher != null)
            {
                return _dispatcher;
            }

            var host = new GameObject("BiomeSourcePatchDispatcher") { hideFlags = HideFlags.HideAndDontSave };
            _dispatcher = host.AddComponent<UnityMainThreadDispatcher>();
            return _dispatcher;
        }
    }
}
