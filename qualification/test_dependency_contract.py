import copy
import unittest
from pathlib import Path

from dependency_contract import (
    ContractError,
    load_inventory,
    require_sha,
    validate_inventory,
    verify_candidate,
)


ROOT = Path(__file__).resolve().parents[1]


class DependencyContractTests(unittest.TestCase):
    def test_candidate_package_satisfies_frozen_contract(self) -> None:
        verify_candidate(ROOT)

    def test_sha_guard_rejects_mutant_digest(self) -> None:
        blob = ROOT / "Assets/Plugins/Harmony/net48/0Harmony.dll.bytes"
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

    def test_inventory_rejects_unreviewed_binding_unification(self) -> None:
        data = load_inventory(ROOT)
        mutant = copy.deepcopy(data)
        mutant["allowed_reference_unifications"][
            "System.Memory|System.Buffers|4.0.2.0"
        ] = "4.0.4.0"
        with self.assertRaisesRegex(ContractError, "unifications"):
            validate_inventory(mutant)


if __name__ == "__main__":
    unittest.main()
