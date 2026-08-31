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
    /// 04-PARETO-COMPLETION-HANDOFF.md SS6 P0-60): forces six FSR
    /// preferences to a known-safe target value and reads each back, and
    /// only registers the provider when every one of them is confirmed.
    /// Fail-closed: on any unconfirmed target, the provider is never
    /// registered (capability stays Unavailable; mutation ON is impossible)
    /// and this never retries on a later tick -- there is no second
    /// initializer.
    ///
    /// The sixth target (StopShowingAutoReloadEnabledDialogBox, forced to
    /// True, not False like the other five) exists because
    /// FastScriptReloadWelcomeScreenInitializer.EnsureUserAwareOfAutoRefresh
    /// (Assets/Scripts/Editor/FastScriptReloadWelcomeScreen.cs, ~L975-1010)
    /// calls the modal EditorUtility.DisplayDialogComplex synchronously
    /// inside its own InitializeOnLoad-attributed static constructor when this
    /// preference is left False on a clean profile with the Editor's own
    /// asset auto-refresh enabled -- a headed Editor blocks on that modal
    /// forever with no CI listener to click it. CI matrix diagnosis.
    ///
    /// Known-unserviced: the same class's
    /// DisplayMessageIfLastDetourPotentiallyCrashedEditor (~L1038) can show
    /// a second, unrelated dialog after a prior crashed detour. This guard
    /// does not cover it -- CI pre-seeds a clean detour-crash marker before
    /// launch instead. Not in scope for this fork; do not add a seventh
    /// switch for it here without a fresh diagnosis.
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
            yield return new PreferenceModeSwitch(
                "stop-showing-auto-reload-enabled-dialog-box",
                FastScriptReloadPreference.StopShowingAutoReloadEnabledDialogBox,
                targetValue: true);
        }

        private sealed class PreferenceModeSwitch : IAutomaticModeSwitch
        {
            private readonly ToggleProjectEditorPreferenceDefinition _preference;
            private readonly bool _targetValue;

            public string Name { get; }

            public PreferenceModeSwitch(string name, ToggleProjectEditorPreferenceDefinition preference, bool targetValue = false)
            {
                Name = name;
                _preference = preference;
                _targetValue = targetValue;
            }

            public void Disable() => _preference.SetEditorPersistedValue(_targetValue);

            public bool IsDisabled() => (bool)_preference.GetEditorPersistedValueOrDefault() == _targetValue;
        }
    }
}
