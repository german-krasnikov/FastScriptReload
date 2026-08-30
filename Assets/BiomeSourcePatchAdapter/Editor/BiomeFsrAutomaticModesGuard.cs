using System.Collections.Generic;
using FastScriptReload.Editor;
using ImmersiveVRTools.Editor.Common.WelcomeScreen.PreferenceDefinition;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.SourcePatch;

namespace Biome.SourcePatch.FSRAdapter
{
    /// <summary>
    /// The one ordered initializer hook for the Biome Source Patch FSR
    /// adapter (Plans/HotReload/V2/FSR-MVP-CLEAN/
    /// 04-PARETO-COMPLETION-HANDOFF.md SS6 P0-60): disables and reads back
    /// the five FSR automatic reload modes, and only registers the provider
    /// when every one of them is confirmed disabled. Fail-closed: on any
    /// unconfirmed mode, the provider is never registered (capability stays
    /// Unavailable; mutation ON is impossible) and this never retries on a
    /// later tick -- there is no second initializer.
    /// </summary>
    [InitializeOnLoad]
    internal static class BiomeFsrAutomaticModesGuard
    {
        internal const string ProviderId = "biome.sourcepatch.fsr-adapter";

        static BiomeFsrAutomaticModesGuard()
        {
            var result = AutomaticModeGuardLogic.DisableAndVerify(BuildModeSwitches());
            if (!result.AllConfirmed)
            {
                Debug.LogWarning(
                    "Biome Source Patch: FSR automatic reload modes not confirmed disabled ("
                    + string.Join(", ", result.UnconfirmedModes)
                    + "). The provider will not be registered; mutation ON stays unavailable.");
                return;
            }

            SourcePatchProviderSlot.Register(ProviderId, new BiomeFsrSourcePatchProvider());
        }

        private static IEnumerable<IAutomaticModeSwitch> BuildModeSwitches()
        {
#pragma warning disable 0618 // EnableCustomFileWatcher is obsolete but still a live automatic mode.
            yield return new PreferenceModeSwitch(
                "enable-auto-reload-for-changed-files", FastScriptReloadPreference.EnableAutoReloadForChangedFiles);
            yield return new PreferenceModeSwitch(
                "enable-on-demand-reload", FastScriptReloadPreference.EnableOnDemandReload);
            yield return new PreferenceModeSwitch(
                "watch-only-specified", FastScriptReloadPreference.WatchOnlySpecified);
            yield return new PreferenceModeSwitch(
                "enable-experimental-editor-hot-reload-support", FastScriptReloadPreference.EnableExperimentalEditorHotReloadSupport);
            yield return new PreferenceModeSwitch(
                "enable-custom-file-watcher", FastScriptReloadPreference.EnableCustomFileWatcher);
#pragma warning restore 0618
        }

        private sealed class PreferenceModeSwitch : IAutomaticModeSwitch
        {
            private readonly ToggleProjectEditorPreferenceDefinition _preference;

            public string Name { get; }

            public PreferenceModeSwitch(string name, ToggleProjectEditorPreferenceDefinition preference)
            {
                Name = name;
                _preference = preference;
            }

            public void Disable() => _preference.SetEditorPersistedValue(false);

            public bool IsDisabled() => !(bool)_preference.GetEditorPersistedValueOrDefault();
        }
    }
}
