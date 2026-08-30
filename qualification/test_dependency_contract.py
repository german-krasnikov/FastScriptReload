import copy
import unittest
from pathlib import Path

from dependency_contract import (
    ContractError,
    load_inventory,
    require_sha,
    validate_inventory,
    validate_loader_source,
    verify_candidate,
    verify_source_candidate,
)

ROOT = Path(__file__).resolve().parents[1]


class DependencyContractTests(unittest.TestCase):
    def test_candidate_package_satisfies_frozen_contract(self) -> None:
        verify_candidate(ROOT)

    def test_sha_guard_rejects_mutant_digest(self) -> None:
        blob = ROOT / "Assets/Plugins/Harmony/Editor/0Harmony.dll.bytes"
        with self.assertRaises(ContractError):
            require_sha(blob, "0" * 64)

    def test_inventory_rejects_duplicate_simple_name(self) -> None:
        data = load_inventory(ROOT)
        mutant = copy.deepcopy(data)
        mutant["managed_assets"].append(copy.deepcopy(mutant["managed_assets"][0]))
        with self.assertRaisesRegex(ContractError, "duplicate"):
            validate_inventory(mutant)

    def test_inventory_rejects_incomplete_asset(self) -> None:
        data = load_inventory(ROOT)
        mutant = copy.deepcopy(data)
        del mutant["managed_assets"][0]["mvid"]
        with self.assertRaisesRegex(ContractError, "Incomplete"):
            validate_inventory(mutant)

    def test_inventory_rejects_mutated_harmony_package_version(self) -> None:
        data = load_inventory(ROOT)
        mutant = copy.deepcopy(data)
        mutant["harmony_blob"]["package_version"] = "2.4.1"
        with self.assertRaisesRegex(ContractError, "Harmony blob contract"):
            validate_inventory(mutant)

    def test_inventory_rejects_unreviewed_binding_unification(self) -> None:
        data = load_inventory(ROOT)
        mutant = copy.deepcopy(data)
        mutant["allowed_reference_unifications"][
            "System.Memory|System.Buffers|4.0.2.0"
        ] = "4.0.4.0"
        with self.assertRaisesRegex(ContractError, "unifications"):
            validate_inventory(mutant)

    def test_inventory_rejects_a_fourth_binding_unification(self) -> None:
        data = load_inventory(ROOT)
        mutant = copy.deepcopy(data)
        mutant["allowed_reference_unifications"][
            "System.Memory|System.Numerics.Vectors|4.1.3.0"
        ] = "4.1.4.0"
        with self.assertRaisesRegex(ContractError, "unifications"):
            validate_inventory(mutant)

    def test_source_candidate_satisfies_frozen_loader_contract(self) -> None:
        verify_source_candidate(ROOT)

    def test_loader_contract_rejects_mutated_frozen_terms(self) -> None:
        source = (ROOT / "Assets/Scripts/Runtime/Polyfills.cs").read_text(
            encoding="utf-8-sig"
        )
        mutations = {
            "guid": ("494e757c92cba704db1d95279f80a30f", "0" * 32),
            "sha": (
                "77e6901ecc606aec66c2a972782a3779e4f50c037d2d165eb7ececdd4d8f794d",
                "0" * 64,
            ),
            "mvid": (
                "B9E6CF65-9433-482B-8860-83CFF28D0128",
                "00000000-0000-0000-0000-000000000000",
            ),
            "version": ("new Version(2, 4, 2, 0)", "new Version(2, 4, 1, 0)"),
            "type": ('"HarmonyLib.PatchTools"', '"HarmonyLib.OtherTools"'),
            "method": ('"DetourMethod"', '"OtherMethod"'),
            "signature": (
                "parameters[1].ParameterType == typeof(MethodBase)",
                "parameters[1].ParameterType == typeof(MethodInfo)",
            ),
        }
        for label, (original, replacement) in mutations.items():
            with self.subTest(label=label):
                mutant = source.replace(original, replacement)
                with self.assertRaisesRegex(ContractError, "loader terms"):
                    validate_loader_source(mutant)


if __name__ == "__main__":
    unittest.main()
