import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = (
    ROOT
    / "Assets/Plugins/ImmersiveVrToolsCommon/ImmersiveVRTools.Common.Runtime.dll"
)
HARNESS = ROOT / "qualification/MethodIdentityHarness.cs"
LOADER = ROOT / "Assets/Scripts/Runtime/AssemblyChangesLoader.cs"
UNITY_MANAGED = Path(
    "/Applications/Unity/Hub/Editor/6000.0.65f1-arm64/Unity.app/Contents/Managed"
)


class MethodIdentityHarnessTests(unittest.TestCase):
    def test_runtime_loader_uses_compile_visible_method_identity(self) -> None:
        source = LOADER.read_text(encoding="utf-8-sig")
        self.assertNotIn("FullDescription()", source)
        self.assertIn("createdTypeMethodToUpdate.ResolveFullName()", source)
        self.assertIn("m.ResolveFullName()", source)

    def test_existing_and_patched_methods_have_exact_matching_identities(self) -> None:
        if shutil.which("mcs") is None or shutil.which("mono") is None:
            self.fail("Mono compiler/runtime are required for the method identity gate")
        with tempfile.TemporaryDirectory(prefix="fsr-method-identity-") as temp:
            output = Path(temp) / "MethodIdentityHarness.exe"
            mono_root = Path(shutil.which("mcs")).resolve().parent.parent
            netstandard = (
                mono_root / "lib/mono/4.7.1-api/Facades/netstandard.dll"
            )
            self.assertTrue(netstandard.is_file(), "Mono netstandard facade is required")
            subprocess.run(
                [
                    "mcs", f"-r:{netstandard}", f"-r:{RUNTIME}",
                    f"-out:{output}", str(HARNESS),
                ],
                cwd=ROOT,
                check=True,
                capture_output=True,
                text=True,
            )
            environment = {
                **os.environ,
                "MONO_PATH": os.pathsep.join((
                    str(RUNTIME.parent),
                    str(UNITY_MANAGED),
                    str(UNITY_MANAGED / "UnityEngine"),
                )),
            }
            result = subprocess.run(
                ["mono", str(output)],
                cwd=ROOT,
                env=environment,
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(result.stdout.strip(), "METHOD-IDENTITIES-VALID")


if __name__ == "__main__":
    unittest.main()
