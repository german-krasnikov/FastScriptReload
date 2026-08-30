import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GATE = ROOT / "Assets/BiomeSourcePatchAdapter/Editor/BiomeSingleFlightGate.cs"
HARNESS = ROOT / "qualification/SingleFlightGateHarness.cs"


class SingleFlightGateHarnessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if shutil.which("mcs") is None or shutil.which("mono") is None:
            raise AssertionError("Mono compiler/runtime are required for the single-flight gate gate")
        cls.temp_dir = tempfile.TemporaryDirectory(prefix="fsr-single-flight-")
        cls.output = Path(cls.temp_dir.name) / "SingleFlightGateHarness.exe"
        subprocess.run(
            ["mcs", "-langversion:latest", f"-out:{cls.output}", str(GATE), str(HARNESS)],
            cwd=ROOT, check=True, capture_output=True, text=True,
        )

    @classmethod
    def tearDownClass(cls) -> None:
        cls.temp_dir.cleanup()

    def run_mode(self, mode: str) -> str:
        result = subprocess.run(
            ["mono", str(self.output), mode], cwd=ROOT, check=False, capture_output=True, text=True,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def test_sequential_enter_exit_enter_succeeds(self) -> None:
        self.assertEqual(self.run_mode("sequential"), "SEQUENTIAL-OK")

    def test_reentrant_call_before_exit_is_rejected(self) -> None:
        self.assertEqual(self.run_mode("reentrant"), "REJECTED:reentrant")

    def test_concurrent_callers_exactly_one_wins(self) -> None:
        self.assertEqual(self.run_mode("concurrent"), "CONCURRENT-EXACTLY-ONE")


if __name__ == "__main__":
    unittest.main()
