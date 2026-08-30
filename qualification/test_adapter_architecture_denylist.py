"""Static/structural architecture guards for the P0-60 fork adapter.

These do not require Unity: they inspect the adapter asmdef JSON and .cs
source text directly. Runtime/behavioral proof of the same boundaries lives
in the offline harnesses (test_automatic_mode_guard_harness.py,
test_single_flight_gate_harness.py, test_body_only_classifier_harness.py,
test_exact_target_loader_harness.py) and, later, in Unity NUnit tests.
"""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ADAPTER_DIR = ROOT / "Assets/BiomeSourcePatchAdapter/Editor"
ASMDEF_PATH = ADAPTER_DIR / "Biome.SourcePatch.FSRAdapter.asmdef"

# GUIDs are read from the actual .meta files rather than hardcoded, so this
# test fails loudly (not silently) if either upstream file's identity ever
# changes.
FSR_RUNTIME_ASMDEF_META = ROOT / "Assets/Scripts/Runtime/FastScriptReload.Runtime.asmdef.meta"
FSR_EDITOR_ASMDEF_META = ROOT / "Assets/Scripts/Editor/FastScriptReload.Editor.asmdef.meta"

# The base UnityMCP.Editor main assembly is a sibling repository's asmdef;
# there is no local .meta to read it from, so its GUID is pinned here as a
# frozen fact from Plans/HotReload/V2/FSR-MVP-CLEAN/
# 04-PARETO-COMPLETION-HANDOFF.md (unity-plugin/Editor/UnityMCP.Editor.asmdef.meta).
MAIN_ASSEMBLY_NAME = "UnityMCP.Editor"
MAIN_ASSEMBLY_GUID = "2128806ed8ce24d1097ed19c6ddaabbc"
NEUTRAL_ASMDEF_NAME = "UnityMCP.Editor.SourcePatch"


def _guid_from_meta(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    match = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", text, re.MULTILINE)
    if not match:
        raise AssertionError(f"Could not read guid from {path}")
    return match.group(1)


class AdapterArchitectureDenylistTests(unittest.TestCase):
    def setUp(self) -> None:
        self.asmdef = json.loads(ASMDEF_PATH.read_text(encoding="utf-8"))
        self.adapter_sources = {
            path: path.read_text(encoding="utf-8")
            for path in ADAPTER_DIR.glob("*.cs")
        }

    def test_asmdef_references_are_exactly_the_two_conceptual_edges(self) -> None:
        # Interpretation fixed by orchestrator approval: "one base-package
        # dependency edge" is one CONCEPTUAL edge onto the co-versioned FSR
        # base package, which ships as two sibling asmdefs (Runtime +
        # Editor) because Unity asmdef references are non-transitive --
        # Biome.SourcePatch.FSRAdapter.asmdef needs types from both directly
        # (AssemblyChangesLoader lives in Runtime, DynamicAssemblyCompiler
        # lives in Editor). Plus exactly one neutral-asmdef reference. No
        # other reference is permitted.
        expected = {
            f"GUID:{_guid_from_meta(FSR_RUNTIME_ASMDEF_META)}",
            f"GUID:{_guid_from_meta(FSR_EDITOR_ASMDEF_META)}",
            NEUTRAL_ASMDEF_NAME,
        }
        actual = set(self.asmdef["references"])
        self.assertEqual(actual, expected)

    def test_asmdef_never_references_main_assembly_by_guid_or_name(self) -> None:
        references = self.asmdef["references"]
        self.assertNotIn(MAIN_ASSEMBLY_NAME, references)
        self.assertNotIn(f"GUID:{MAIN_ASSEMBLY_GUID}", references)

    def test_adapter_source_never_uses_the_main_assembly_namespace(self) -> None:
        # Exact "using UnityMCP.Editor;" only -- must not false-positive on
        # the legitimate "using UnityMCP.Editor.SourcePatch;".
        pattern = re.compile(r"using\s+UnityMCP\.Editor\s*;")
        for path, text in self.adapter_sources.items():
            self.assertNotRegex(text, pattern, f"{path} references the main assembly namespace")

    def test_exactly_one_initializeonload_hook_in_the_adapter(self) -> None:
        count = sum(text.count("[InitializeOnLoad]") for text in self.adapter_sources.values())
        self.assertEqual(count, 1)

    def test_exactly_one_slot_register_call(self) -> None:
        count = sum(text.count("SourcePatchProviderSlot.Register(") for text in self.adapter_sources.values())
        self.assertEqual(count, 1)

    def test_exactly_one_direct_compile_call(self) -> None:
        count = sum(text.count("DynamicAssemblyCompiler.Compile(") for text in self.adapter_sources.values())
        self.assertEqual(count, 1)

    def test_no_manager_queue_events_or_debounce_are_touched(self) -> None:
        forbidden_identifiers = (
            "FastScriptReloadManager",
            "EditorApplication.update",
            "TriggerReloadForChangedFiles",
            "TriggerDomainReloadIfOverNDynamicallyLoadedAssembles",
            "HotReloadFailed",
            "HotReloadSucceeded",
            "AddFileChangeToProcess",
            "_dynamicFileHotReloadStateEntries",
            "AssemblyChangesLoaderResolver",
            "McsExeDynamicCompilation",
            "FastScriptReload_CompileViaMCS",
        )
        combined = "\n".join(self.adapter_sources.values())
        found = [identifier for identifier in forbidden_identifiers if identifier in combined]
        self.assertEqual(found, [])

    def test_no_second_asmdef_or_watcher_files_entered_the_adapter_folder(self) -> None:
        asmdefs = list(ADAPTER_DIR.glob("*.asmdef"))
        self.assertEqual(len(asmdefs), 1)
        self.assertEqual(asmdefs[0].name, "Biome.SourcePatch.FSRAdapter.asmdef")
        forbidden_name_fragments = ("Watcher", "Welcome", "Window")
        for path in self.adapter_sources:
            for fragment in forbidden_name_fragments:
                self.assertNotIn(fragment, path.name)

    def test_adapter_asmdef_is_editor_only(self) -> None:
        self.assertEqual(self.asmdef["includePlatforms"], ["Editor"])

    def test_adapter_asmdef_is_not_auto_referenced(self) -> None:
        # Mirrors UnityMCP.Editor.SourcePatch.asmdef's own convention: a leaf
        # adapter nothing else needs to auto-reference.
        self.assertFalse(self.asmdef["autoReferenced"])


if __name__ == "__main__":
    unittest.main()
