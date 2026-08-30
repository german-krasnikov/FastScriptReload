using Biome.SourcePatch.FSRAdapter;
using NUnit.Framework;

namespace FastScriptReload.Tests.Editor.Integration.BiomeSourcePatchAdapter
{
    /// <summary>
    /// NUnit mirror of qualification/test_single_flight_gate_harness.py.
    /// </summary>
    public class BiomeSingleFlightGateTests
    {
        [Test]
        public void TryEnter_SequentialEnterExitEnter_Succeeds()
        {
            var gate = new BiomeSingleFlightGate();

            Assert.IsTrue(gate.TryEnter());
            gate.Exit();
            Assert.IsTrue(gate.TryEnter());
        }

        [Test]
        public void TryEnter_ReentrantBeforeExit_IsRejected()
        {
            var gate = new BiomeSingleFlightGate();

            Assert.IsTrue(gate.TryEnter());
            Assert.IsFalse(gate.TryEnter());
        }
    }
}
