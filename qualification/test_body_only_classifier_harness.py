import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CLASSIFIER = ROOT / "Assets/BiomeSourcePatchAdapter/Editor/BiomeBodyOnlyMethodClassifier.cs"
HARNESS = ROOT / "qualification/BodyOnlyClassifierHarness.cs"
ROSLYN_DIR = ROOT / "Assets/Plugins/Roslyn/2021+"


def _netstandard_facade() -> Path:
    mcs_path = Path(shutil.which("mcs")).resolve()
    # mcs -> <mono-root>/bin/mcs ; facade lives under lib/mono/<profile>/Facades
    mono_root = mcs_path.parent.parent
    candidate = mono_root / "lib/mono/4.7.1-api/Facades/netstandard.dll"
    if not candidate.is_file():
        raise AssertionError(f"Mono netstandard facade not found at {candidate}")
    return candidate


class BodyOnlyClassifierHarnessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if shutil.which("mcs") is None or shutil.which("mono") is None:
            raise AssertionError("Mono compiler/runtime are required for the body-only classifier gate")
        netstandard = _netstandard_facade()
        cls.temp_dir = tempfile.TemporaryDirectory(prefix="fsr-classifier-")
        cls.output = Path(cls.temp_dir.name) / "BodyOnlyClassifierHarness.exe"
        subprocess.run(
            [
                "mcs", "-langversion:latest",
                f"-r:{netstandard}",
                f"-r:{ROSLYN_DIR / 'Microsoft.CodeAnalysis.dll'}",
                f"-r:{ROSLYN_DIR / 'Microsoft.CodeAnalysis.CSharp.dll'}",
                f"-r:{ROSLYN_DIR / 'System.Collections.Immutable.dll'}",
                f"-out:{cls.output}", str(CLASSIFIER), str(HARNESS),
            ],
            cwd=ROOT,
            check=True,
            capture_output=True,
            text=True,
        )
        cls.env = {"MONO_PATH": str(ROSLYN_DIR)}

    @classmethod
    def tearDownClass(cls) -> None:
        cls.temp_dir.cleanup()

    def run_mode(self, mode: str) -> str:
        import os
        env = {**os.environ, **self.env}
        result = subprocess.run(
            ["mono", str(self.output), mode],
            cwd=ROOT,
            env=env,
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def test_admits_body_only_change_on_existing_instance_method(self) -> None:
        self.assertEqual(self.run_mode("admit-body-only"), "ADMITTED:Foo.Bar")

    def test_admits_body_only_change_on_static_method(self) -> None:
        self.assertEqual(self.run_mode("admit-static-method"), "ADMITTED:Foo.Bar")

    def test_admits_body_only_change_on_expression_bodied_method(self) -> None:
        self.assertEqual(self.run_mode("admit-expression-body"), "ADMITTED:Foo.Bar")

    def test_rejects_when_no_method_body_changed(self) -> None:
        self.assertEqual(self.run_mode("reject-no-change"), "REJECTED:no-body-change")

    def test_rejects_new_method_added(self) -> None:
        self.assertEqual(self.run_mode("reject-new-method"), "REJECTED:method-count-changed")

    def test_rejects_signature_changed(self) -> None:
        self.assertEqual(self.run_mode("reject-signature-changed"), "REJECTED:signature-changed")

    def test_rejects_attribute_changed(self) -> None:
        self.assertEqual(self.run_mode("reject-attribute-changed"), "REJECTED:signature-changed")

    def test_rejects_generic_method(self) -> None:
        self.assertEqual(self.run_mode("reject-generic-method"), "REJECTED:generic-method")

    def test_rejects_generic_containing_type(self) -> None:
        self.assertEqual(self.run_mode("reject-generic-type"), "REJECTED:generic-type")

    def test_rejects_async_method(self) -> None:
        self.assertEqual(self.run_mode("reject-async-method"), "REJECTED:async-method")

    def test_rejects_iterator_method(self) -> None:
        self.assertEqual(self.run_mode("reject-iterator-method"), "REJECTED:iterator-method")

    def test_rejects_lambda_introduced_in_body(self) -> None:
        self.assertEqual(self.run_mode("reject-lambda-introduced"), "REJECTED:closure-shape")

    def test_rejects_local_function_introduced_in_body(self) -> None:
        self.assertEqual(self.run_mode("reject-local-function-introduced"), "REJECTED:closure-shape")

    def test_rejects_when_more_than_one_method_body_changed(self) -> None:
        self.assertEqual(self.run_mode("reject-multiple-methods-changed"), "REJECTED:multiple-methods-changed")

    def test_rejects_syntax_error_in_new_source(self) -> None:
        self.assertEqual(self.run_mode("reject-syntax-error"), "REJECTED:syntax-error")

    def test_rejects_field_addition_as_no_body_change(self) -> None:
        self.assertEqual(self.run_mode("reject-field-added"), "REJECTED:no-body-change")

    def test_admits_and_reports_full_namespace_qualified_type_name(self) -> None:
        self.assertEqual(self.run_mode("admit-with-namespace"), "ADMITTED:My.Deep.Namespace.Foo.Bar")

    def test_rejects_nested_type_method(self) -> None:
        self.assertEqual(self.run_mode("reject-nested-type"), "REJECTED:nested-type")


if __name__ == "__main__":
    unittest.main()
