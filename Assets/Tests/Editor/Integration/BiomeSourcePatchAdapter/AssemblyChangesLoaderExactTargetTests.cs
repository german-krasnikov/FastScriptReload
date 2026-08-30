using System;
using System.Collections.Generic;
using System.IO;
using FastScriptReload.Editor.Compilation;
using FastScriptReload.Runtime;
using FastScriptReload.Tests.Runtime.Integration.CodePatterns;
using ImmersiveVRTools.Runtime.Common;
using NUnit.Framework;
using UnityEngine;

namespace FastScriptReload.Tests.Editor.Integration.BiomeSourcePatchAdapter
{
    /// <summary>
    /// Real-Unity proof for the P0-60 exact-target loader overload
    /// (Plans/HotReload/V2/FSR-MVP-CLEAN/04-PARETO-COMPLETION-HANDOFF.md
    /// SS6): a real Memory.DetourMethod call cannot be safely exercised
    /// outside Unity's own Mono runtime (see qualification/
    /// test_exact_target_loader_harness.py's header comment for the native
    /// crash that proved this), so this is where the actual detour, skip
    /// and lifecycle-bypass behavior gets its terminal proof. Runs only
    /// when FAST_SCRIPT_RELOAD_INCLUDE_TESTS is defined, in a disposable
    /// worker (P0-80).
    /// </summary>
    public class AssemblyChangesLoaderExactTargetTests
    {
        private static readonly string FixtureRelativePath =
            Path.Combine("Assets", "Tests", "Runtime", "Integration", "CodePatterns", "ExactTargetPatchFixture.cs");

        private string _originalSource;
        private string _fixtureFullPath;

        [SetUp]
        public void SetUp()
        {
            _fixtureFullPath = Path.Combine(Directory.GetCurrentDirectory(), FixtureRelativePath);
            _originalSource = File.ReadAllText(_fixtureFullPath);
        }

        [TearDown]
        public void TearDown()
        {
            File.WriteAllText(_fixtureFullPath, _originalSource);
        }

        [Test]
        public void DynamicallyUpdateSingleMethodForCreatedAssembly_BodyOnlyChange_DetoursOnlyTheTargetMethod()
        {
            var mutatedSource = _originalSource.Replace(
                "public int Changing(int x)\n        {\n            return x;\n        }",
                "public int Changing(int x)\n        {\n            return x + 100;\n        }");
            Assert.AreNotEqual(_originalSource, mutatedSource, "test precondition: mutation must actually change the source");
            File.WriteAllText(_fixtureFullPath, mutatedSource);

            var dispatcher = new GameObject("ExactTargetDispatcher").AddComponent<UnityMainThreadDispatcher>();
            var compileResult = DynamicAssemblyCompiler.Compile(new List<string> { _fixtureFullPath }, dispatcher);
            Assert.IsFalse(compileResult.IsError, "fixture must compile cleanly");

            var existingMethod = typeof(ExactTargetPatchFixture).GetMethod("Changing");
            var result = AssemblyChangesLoader.Instance.DynamicallyUpdateSingleMethodForCreatedAssembly(
                compileResult.CompiledAssembly, existingMethod);

            Assert.IsTrue(result.Applied, result.FailureReason);
            Assert.AreEqual(1, result.SkippedMethodIdentities.Count, "exactly Other must be skipped, never detoured");

            var instance = new ExactTargetPatchFixture();
            Assert.AreEqual(105, instance.Changing(5), "target method must be detoured");
            Assert.AreEqual(5, instance.Other(5), "sibling method must never be detoured");
        }

        [Test]
        public void ResolveExactTarget_MethodNotPresentInCreatedType_FailsClosedWithoutDetouring()
        {
            var mutatedSource = _originalSource.Replace("Changing", "RenamedAway");
            File.WriteAllText(_fixtureFullPath, mutatedSource);

            var dispatcher = new GameObject("ExactTargetDispatcher").AddComponent<UnityMainThreadDispatcher>();
            var compileResult = DynamicAssemblyCompiler.Compile(new List<string> { _fixtureFullPath }, dispatcher);
            Assert.IsFalse(compileResult.IsError, "fixture must still compile cleanly after the rename");

            var existingMethod = typeof(ExactTargetPatchFixture).GetMethod("Changing");
            var resolution = AssemblyChangesLoader.ResolveExactTarget(compileResult.CompiledAssembly, existingMethod);

            Assert.IsNotNull(resolution.FailureReason);
            Assert.AreEqual("created-method-not-found", resolution.FailureReason);
        }
    }
}
