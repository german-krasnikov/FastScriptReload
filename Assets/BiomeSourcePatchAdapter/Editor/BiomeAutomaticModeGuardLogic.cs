using System.Collections.Generic;

namespace Biome.SourcePatch.FSRAdapter
{
    /// <summary>
    /// One automatic FSR reload mode this guard must disable and verify
    /// before the provider registers (Plans/HotReload/V2/FSR-MVP-CLEAN/
    /// 04-PARETO-COMPLETION-HANDOFF.md P0-60). Kept engine-neutral -- no
    /// Unity/FSR types -- so <see cref="AutomaticModeGuardLogic"/> below is
    /// fully offline-testable. The real Unity-facing switches (backed by
    /// FastScriptReloadPreference) live in BiomeFsrAutomaticModesGuard.
    /// </summary>
    internal interface IAutomaticModeSwitch
    {
        string Name { get; }
        void Disable();
        bool IsDisabled();
    }

    /// <summary>
    /// Result of one disable-and-verify pass over every automatic mode.
    /// </summary>
    internal sealed class AutomaticModeGuardResult
    {
        public IReadOnlyList<string> UnconfirmedModes { get; }
        public bool AllConfirmed => UnconfirmedModes.Count == 0;

        public AutomaticModeGuardResult(IReadOnlyList<string> unconfirmedModes)
        {
            UnconfirmedModes = unconfirmedModes;
        }
    }

    /// <summary>
    /// Pure decision logic: disable every mode, read each one back, and
    /// report exactly which ones failed to confirm. Every mode is always
    /// attempted -- fail-closed never means "stop early" -- but the caller
    /// (BiomeFsrAutomaticModesGuard) only registers the provider when
    /// <see cref="AutomaticModeGuardResult.AllConfirmed"/> is true, and
    /// never retries on a later tick.
    /// </summary>
    internal static class AutomaticModeGuardLogic
    {
        internal static AutomaticModeGuardResult DisableAndVerify(IEnumerable<IAutomaticModeSwitch> modes)
        {
            var unconfirmed = new List<string>();
            foreach (var mode in modes)
            {
                mode.Disable();
                if (!mode.IsDisabled())
                {
                    unconfirmed.Add(mode.Name);
                }
            }
            return new AutomaticModeGuardResult(unconfirmed);
        }
    }
}
