using System.Threading;

namespace Biome.SourcePatch.FSRAdapter
{
    /// <summary>
    /// Cheap reentrancy guard for ISourcePatchProvider.Apply. The
    /// coordinator already guarantees one in-flight source transaction at a
    /// time (Plans/HotReload/V2/FSR-MVP-CLEAN/04-PARETO-COMPLETION-HANDOFF.md
    /// SS1.1/SS3.1), but this closes the gap defensively on the engine side
    /// if that contract is ever violated by the host: a nested/concurrent
    /// Apply call must never reach Compile.
    /// </summary>
    internal sealed class BiomeSingleFlightGate
    {
        private int _occupied;

        internal bool TryEnter() => Interlocked.CompareExchange(ref _occupied, 1, 0) == 0;

        internal void Exit() => Interlocked.Exchange(ref _occupied, 0);
    }
}
