"""Offline proof for the P0-60 exact-target loader overload.

This intentionally never triggers a real Memory.DetourMethod call: a bare
`mono` CLI process is not Unity's own Mono runtime, and a live Harmony
detour attempted outside it crashed natively during development (a native
`gpath.c` assertion). qualification/test_loader_harness.py already follows
the same discipline for the base loader contract -- it proves the Harmony
blob loads and exposes the right identity, but never performs a live detour
offline. So this module compiles the REAL, modified
Assets/Scripts/Runtime/AssemblyChangesLoader.cs (plus its real Runtime
siblings) and exercises AssemblyChangesLoader.ResolveExactTarget -- the pure
selection/rejection step -- plus the public overload's failure-delegation
path, which never reaches Memory.DetourMethod. The actual detour, and the
lifecycle-bypass proof, are exercised for real only in Unity (P0-80 and
Assets/Tests/Editor/Integration/BiomeSourcePatchAdapter/
AssemblyChangesLoaderExactTargetTests.cs).
"""
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RUNTIME_SCRIPTS = ROOT / "Assets/Scripts/Runtime"
IMMERSIVE_RUNTIME = ROOT / "Assets/Plugins/ImmersiveVrToolsCommon/ImmersiveVRTools.Common.Runtime.dll"
LOADER_HARNESS = ROOT / "qualification/LoaderHarness.cs"
PATCHED_FIXTURES = ROOT / "qualification/ExactTargetLoaderPatchedFixtures.cs"
HARNESS = ROOT / "qualification/ExactTargetLoaderHarness.cs"


def _netstandard_facade() -> Path:
    mcs_path = Path(shutil.which("mcs")).resolve()
    mono_root = mcs_path.parent.parent
    candidate = mono_root / "lib/mono/4.7.1-api/Facades/netstandard.dll"
    if not candidate.is_file():
        raise AssertionError(f"Mono netstandard facade not found at {candidate}")
    return candidate


class ExactTargetLoaderHarnessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if shutil.which("mcs") is None or shutil.which("mono") is None:
            raise AssertionError("Mono compiler/runtime are required for the exact-target loader gate")

        cls.temp_dir = tempfile.TemporaryDirectory(prefix="fsr-exact-target-")
        temp_dir = Path(cls.temp_dir.name)
        netstandard = _netstandard_facade()

        cls.unity_stub = temp_dir / "UnityEngine.CoreModule.dll"
        subprocess.run(
            ["mcs", "-langversion:latest", "-target:library", f"-out:{cls.unity_stub}",
             str(Path(__file__).with_name("UnityEngineCoreModuleStub.cs"))],
            cwd=ROOT, check=True, capture_output=True, text=True,
        )

        cls.patched_fixtures = temp_dir / "ExactTargetLoaderPatchedFixtures.dll"
        subprocess.run(
            ["mcs", "-langversion:latest", "-target:library", f"-out:{cls.patched_fixtures}", str(PATCHED_FIXTURES)],
            cwd=ROOT, check=True, capture_output=True, text=True,
        )

        cls.output = temp_dir / "ExactTargetLoaderHarness.exe"
        subprocess.run(
            [
                "mcs", "-langversion:latest",
                "-define:UNITY_EDITOR,LiveScriptReload_Enabled",
                "-main:ExactTargetLoaderHarness",
                f"-r:{netstandard}", f"-r:{IMMERSIVE_RUNTIME}",
                f"-r:{cls.unity_stub}", f"-r:{cls.patched_fixtures}",
                f"-out:{cls.output}",
                str(RUNTIME_SCRIPTS / "Polyfills.cs"),
                str(RUNTIME_SCRIPTS / "DetourCrashHandler.cs"),
                str(RUNTIME_SCRIPTS / "ProjectTypeCache.cs"),
                str(RUNTIME_SCRIPTS / "AssemblyChangesLoader.cs"),
                str(LOADER_HARNESS),
                str(HARNESS),
            ],
            cwd=ROOT, check=True, capture_output=True, text=True,
        )
        cls.mono_path = os.pathsep.join((
            str(IMMERSIVE_RUNTIME.parent), str(temp_dir),
        ))

    @classmethod
    def tearDownClass(cls) -> None:
        cls.temp_dir.cleanup()

    def run_mode(self, mode: str) -> str:
        env = {**os.environ, "MONO_PATH": self.mono_path}
        result = subprocess.run(
            ["mono", str(self.output), mode], cwd=ROOT, env=env,
            check=False, capture_output=True, text=True,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def test_resolves_exactly_one_target_and_skips_every_sibling_method(self) -> None:
        self.assertEqual(self.run_mode("resolves-exactly-one"), "RESOLVED:Changing")

    def test_rejects_when_created_type_lacks_the_target_method(self) -> None:
        self.assertEqual(self.run_mode("missing-created-method"), "REJECTED:created-method-not-found")

    def test_rejects_when_no_created_type_matches(self) -> None:
        self.assertEqual(self.run_mode("type-not-found"), "REJECTED:created-type-not-found")

    def test_rejects_generic_existing_type(self) -> None:
        self.assertEqual(self.run_mode("generic-type-rejected"), "REJECTED:generic-type")

    def test_rejects_generic_method(self) -> None:
        self.assertEqual(self.run_mode("generic-method-rejected"), "REJECTED:generic-method")

    def test_rejects_ambiguous_created_type_match(self) -> None:
        self.assertEqual(self.run_mode("ambiguous-created-type"), "REJECTED:created-type-ambiguous")

    def test_public_overload_delegates_failure_without_reaching_detour(self) -> None:
        self.assertEqual(
            self.run_mode("public-overload-delegates-failure-without-detour"),
            "REJECTED:created-type-not-found",
        )


if __name__ == "__main__":
    unittest.main()
