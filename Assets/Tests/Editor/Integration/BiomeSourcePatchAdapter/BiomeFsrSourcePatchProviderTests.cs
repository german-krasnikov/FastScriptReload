using System.Text;
using Biome.SourcePatch.FSRAdapter;
using NUnit.Framework;
using UnityMCP.Editor.SourcePatch;

namespace FastScriptReload.Tests.Editor.Integration.BiomeSourcePatchAdapter
{
    /// <summary>
    /// Covers the zero-effect rejection paths of BiomeFsrSourcePatchProvider
    /// that never need a real DynamicAssemblyCompiler.Compile call: an
    /// out-of-hard-scope change (classifier rejects before any compile), and
    /// a body-only change whose declaring type is not a loaded project type
    /// (ProjectTypeCache lookup fails before any compile). The real
    /// compile+detour+skip+lifecycle-bypass path is proven in
    /// AssemblyChangesLoaderExactTargetTests, which exercises the engine
    /// directly; BiomeSingleFlightGateTests independently proves the
    /// reentrancy guard this provider composes.
    /// </summary>
    public class BiomeFsrSourcePatchProviderTests
    {
        private static SourcePatchRequest MakeRequest(string before, string after)
        {
            Assert.IsTrue(SourcePatchRequest.TryCreate(
                "Assets/DoesNotMatterForThisTest.cs",
                Encoding.UTF8.GetBytes(before),
                Encoding.UTF8.GetBytes(after),
                out var request));
            return request;
        }

        [Test]
        public void Apply_OutOfHardScopeChange_RejectsWithoutCompiling()
        {
            var provider = new BiomeFsrSourcePatchProvider();
            var request = MakeRequest(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(int x) { return x; } int Baz() { return 1; } }");

            var outcome = provider.Apply(request);

            Assert.AreEqual(SourcePatchApplyOutcome.Rejected, outcome);
        }

        [Test]
        public void Apply_BodyOnlyChangeOnUnknownType_RejectsWithoutCompiling()
        {
            var provider = new BiomeFsrSourcePatchProvider();
            var request = MakeRequest(
                "class BiomeFsrSourcePatchProviderTests_NoSuchLoadedType { int Bar(int x) { return x; } }",
                "class BiomeFsrSourcePatchProviderTests_NoSuchLoadedType { int Bar(int x) { return x + 1; } }");

            var outcome = provider.Apply(request);

            Assert.AreEqual(SourcePatchApplyOutcome.Rejected, outcome);
        }
    }
}
