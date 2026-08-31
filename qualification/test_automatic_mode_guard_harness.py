import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOGIC = ROOT / "Assets/BiomeSourcePatchAdapter/Editor/BiomeAutomaticModeGuardLogic.cs"
HARNESS = ROOT / "qualification/AutomaticModeGuardHarness.cs"


class AutomaticModeGuardHarnessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if shutil.which("mcs") is None or shutil.which("mono") is None:
            raise AssertionError("Mono compiler/runtime are required for the automatic-mode guard gate")
        cls.temp_dir = tempfile.TemporaryDirectory(prefix="fsr-automode-guard-")
        cls.output = Path(cls.temp_dir.name) / "AutomaticModeGuardHarness.exe"
        subprocess.run(
            [
                "mcs", "-langversion:latest",
                f"-out:{cls.output}", str(LOGIC), str(HARNESS),
            ],
            cwd=ROOT,
            check=True,
            capture_output=True,
            text=True,
        )

    @classmethod
    def tearDownClass(cls) -> None:
        cls.temp_dir.cleanup()

    def run_mode(self, mode: str) -> str:
        result = subprocess.run(
            ["mono", str(self.output), mode],
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def test_all_confirmed_allows_registration(self) -> None:
        self.assertEqual(self.run_mode("all-confirmed"), "ALL-CONFIRMED")

    def test_one_unconfirmed_mode_blocks_and_names_it(self) -> None:
        self.assertEqual(
            self.run_mode("one-unconfirmed"),
            "REJECTED:enable-auto-reload-for-changed-files",
        )

    def test_disable_is_attempted_on_every_mode_even_after_earlier_failure(self) -> None:
        self.assertEqual(self.run_mode("no-short-circuit"), "NO-SHORT-CIRCUIT")

    def test_sixth_mode_unconfirmed_blocks_and_names_it(self) -> None:
        # The sixth mode is the welcome-initializer's first-run dialog
        # suppression preference (forced to True, not False like the other
        # five) -- must fail closed identically when it alone does not
        # confirm.
        self.assertEqual(
            self.run_mode("sixth-unconfirmed"),
            "REJECTED:stop-showing-auto-reload-enabled-dialog-box",
        )


if __name__ == "__main__":
    unittest.main()
