import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
POLYFILLS = ROOT / "Assets/Scripts/Runtime/Polyfills.cs"
HARNESS = ROOT / "qualification/LoaderHarness.cs"


class LoaderHarnessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if shutil.which("mcs") is None or shutil.which("mono") is None:
            raise AssertionError("Mono compiler/runtime are required for the frozen loader gate")
        cls.temp_dir = tempfile.TemporaryDirectory(prefix="fsr-loader-contract-")
        cls.output = Path(cls.temp_dir.name) / "FastScriptReload.Editor.exe"
        subprocess.run(
            [
                "mcs", "-define:UNITY_EDITOR", "-langversion:latest",
                f"-out:{cls.output}", str(POLYFILLS), str(HARNESS),
            ],
            cwd=ROOT,
            check=True,
            capture_output=True,
            text=True,
        )

    @classmethod
    def tearDownClass(cls) -> None:
        cls.temp_dir.cleanup()

    def run_mode(self, mode: str, assets_root: Path) -> str:
        result = subprocess.run(
            ["mono", str(self.output), mode, str(assets_root)],
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def test_real_blob_loads_once_and_exposes_exact_contract(self) -> None:
        self.assertEqual(self.run_mode("valid", ROOT / "Assets"), "VALID")

    def test_concurrent_calls_share_one_verified_load(self) -> None:
        self.assertEqual(
            self.run_mode("concurrent", ROOT / "Assets"), "CONCURRENT"
        )

    def test_wrong_asset_path_fails_closed(self) -> None:
        self.assertEqual(self.run_mode("bad-path", ROOT / "Assets"), "REJECTED:bad-path")

    def test_mutated_blob_hash_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory(prefix="fsr-mutant-") as temp_dir:
            mutant_root = Path(temp_dir)
            mutant_blob = mutant_root / "Plugins/Harmony/Editor/0Harmony.dll.bytes"
            mutant_blob.parent.mkdir(parents=True)
            data = bytearray(
                (ROOT / "Assets/Plugins/Harmony/Editor/0Harmony.dll.bytes").read_bytes()
            )
            data[-1] ^= 1
            mutant_blob.write_bytes(data)
            self.assertEqual(
                self.run_mode("bad-hash", mutant_root), "REJECTED:bad-hash"
            )

    def test_preloaded_harmony_fails_closed(self) -> None:
        self.assertEqual(
            self.run_mode("preloaded", ROOT / "Assets"), "REJECTED:preloaded"
        )


if __name__ == "__main__":
    unittest.main()
