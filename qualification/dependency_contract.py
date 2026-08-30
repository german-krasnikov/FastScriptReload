"""Offline contract oracle for the frozen Unity 6 FSR dependency candidate."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
from pathlib import Path
from typing import Any

UPSTREAM = "51140b71d9e5df1de231b33ec20ee089b18bebec"
INVENTORY = Path("Assets/Documentation~/DependencyInventory.json")
HARMONY_BLOB = Path("Assets/Plugins/Harmony/Editor/0Harmony.dll.bytes")
HARMONY_ASSET_PATH = (
    "Packages/com.handzlikchris.fastscriptreload/Plugins/Harmony/Editor/"
    "0Harmony.dll.bytes"
)
HARMONY_SHA256 = "77e6901ecc606aec66c2a972782a3779e4f50c037d2d165eb7ececdd4d8f794d"
HARMONY_MVID = "B9E6CF65-9433-482B-8860-83CFF28D0128"
HARMONY_GUID = "494e757c92cba704db1d95279f80a30f"
EXPECTED_HARMONY_BLOB = {
    "path": str(HARMONY_BLOB),
    "asset_guid": HARMONY_GUID,
    "package_id": "Lib.Harmony",
    "package_version": "2.4.2",
    "package_sha256": "d64592e53090464559fce48612c9ca7c8dc73113841376b7aa3455f46fc5d579",
    "package_asset": "lib/net48/0Harmony.dll",
    "asset_sha256": HARMONY_SHA256,
    "assembly_name": "0Harmony",
    "module_name": "0Harmony",
    "assembly_version": "2.4.2.0",
    "mvid": HARMONY_MVID,
    "license": "MIT",
}
NUMERICS_PACKAGE_ASSET = "lib/netstandard2.0/System.Numerics.Vectors.dll"
ROSLYN_DIR = Path("Assets/Plugins/Roslyn/2021+")
EXPECTED_UNIFICATIONS = {
    "System.Memory|System.Buffers|4.0.2.0": "4.0.3.0",
    "System.Memory|System.Runtime.CompilerServices.Unsafe|4.0.4.1": "6.0.0.0",
    "System.Threading.Tasks.Extensions|System.Runtime.CompilerServices.Unsafe|4.0.4.1": "6.0.0.0",
}
EXPECTED_UNIFICATION_EVIDENCE = {
    "System.Memory|System.Buffers|4.0.2.0": {
        "requesting_package": "System.Memory 4.5.5",
        "selected_package": "System.Buffers 4.5.1",
        "selected_assembly_version": "4.0.3.0",
        "constraint_source": (
            "System.Memory 4.5.5 .NETStandard2.0 nuspec: "
            "System.Buffers >= 4.5.1"
        ),
    },
    "System.Memory|System.Runtime.CompilerServices.Unsafe|4.0.4.1": {
        "requesting_package": "System.Memory 4.5.5",
        "selected_package": "System.Runtime.CompilerServices.Unsafe 6.0.0",
        "selected_assembly_version": "6.0.0.0",
        "constraint_source": (
            "Microsoft.CodeAnalysis.Common 4.6.0 .NETStandard2.0 nuspec: "
            "System.Runtime.CompilerServices.Unsafe >= 6.0.0"
        ),
    },
    "System.Threading.Tasks.Extensions|System.Runtime.CompilerServices.Unsafe|4.0.4.1": {
        "requesting_package": "System.Threading.Tasks.Extensions 4.5.4",
        "selected_package": "System.Runtime.CompilerServices.Unsafe 6.0.0",
        "selected_assembly_version": "6.0.0.0",
        "constraint_source": (
            "Microsoft.CodeAnalysis.Common 4.6.0 .NETStandard2.0 nuspec: "
            "System.Runtime.CompilerServices.Unsafe >= 6.0.0"
        ),
    },
}
EXPECTED_PACKAGE_SHAS = {
    "Microsoft.CodeAnalysis.Common|4.6.0": "e24a168a7888aefe190664bd4996f7df8eca69e8ca2a1d759fb0918fd7e47363",
    "Microsoft.CodeAnalysis.CSharp|4.6.0": "382c54592a2556b98fd9bb36497f562e36ff53c068c7cb354daca4d39fe7dbf9",
    "System.Buffers|4.5.1": "c30b3dd2c7e2f4cee4b823d692fd42118309b42ab1f5007f923d329a5b0d6b12",
    "System.Collections.Immutable|7.0.0": "f5a9f6c1bc6e7b6aabb6e818112f5ac2c85083e29f26a6a386786ce3991021d9",
    "System.Memory|4.5.5": "10f43da352a29fb2b3188e4edd4dcf5100194c8b526e4f61fe2e2b5623775a22",
    "System.Numerics.Vectors|4.4.0": "6ae5d02b67e52ff2699c1feb11c01c526e2f60c09830432258e0809486aabb65",
    "System.Reflection.Metadata|7.0.0": "1b000a4219213c1613aa645d1bd73db5aaab292283c325203848562cac5634f2",
    "System.Runtime.CompilerServices.Unsafe|6.0.0": "6c41b53e70e9eee298cff3a02ce5acdd15b04125589be0273f0566026720a762",
    "System.Text.Encoding.CodePages|7.0.0": "782293570ba60f4e7564472825c0d54469c8180b04bcaa5f1f7c9d2a5b87c66a",
    "System.Threading.Tasks.Extensions|4.5.4": "a304a963cc0796c5179f9c6b7d8022bbce3b2fa7c029eb6196f631f7b462d678",
}
SOURCE_ALLOWLIST = {
    "Assets/Scripts/Runtime/Polyfills.cs",
    "Assets/Scripts/Runtime/AssemblyChangesLoader.cs",
    "Assets/Scripts/Editor/Compilation/DotnetExeCompilator.cs",
    "Assets/Scripts/Editor/AssemblyPostProcess/AddInternalsVisibleToForAllUserAssembliesPostProcess.cs",
    "Assets/Scripts/Editor/FastScriptReloadWelcomeScreen.cs",
    "Assets/Scripts/Editor/NewFields/NewFieldsRendererDefaultEditorPatch.cs",
}
FRAMEWORK_REFERENCES = {
    "mscorlib", "netstandard", "System", "System.Core", "System.Runtime",
    "System.Collections", "System.Diagnostics.Debug", "System.Globalization",
    "System.Linq", "System.Resources.ResourceManager",
    "System.Runtime.Extensions", "System.Runtime.InteropServices",
    "System.Threading", "System.Threading.Tasks",
}


class ContractError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_sha(path: Path, expected: str) -> None:
    actual = sha256(path)
    if actual != expected:
        raise ContractError(f"{path}: SHA-256 {actual}, expected {expected}")


def _monodis(option: str, path: Path) -> str:
    result = subprocess.run(
        ["monodis", option, str(path)], capture_output=True, text=True, check=False,
    )
    if result.returncode != 0:
        raise ContractError(f"monodis {option} failed for {path}: {result.stderr}")
    return result.stdout


def assembly_identity(path: Path) -> tuple[str, str, str]:
    assembly = _monodis("--assembly", path)
    module = _monodis("--module", path)
    name_match = re.search(r"^Name:\s+(.+)$", assembly, re.MULTILINE)
    version_match = re.search(r"^Version:\s+(.+)$", assembly, re.MULTILINE)
    mvid_match = re.search(r"\{([0-9A-Fa-f-]{36})\}", module)
    if not (name_match and version_match and mvid_match):
        raise ContractError(f"Cannot read managed identity for {path}")
    return (
        name_match.group(1).strip(), version_match.group(1).strip(),
        mvid_match.group(1).upper(),
    )


def module_name(path: Path) -> str:
    module = _monodis("--module", path)
    match = re.search(r"^\d+:\s+(\S+)\s+\d+\s+\{", module, re.MULTILINE)
    if not match:
        raise ContractError(f"Cannot read managed module name for {path}")
    return match.group(1)


def assembly_references(path: Path) -> dict[str, str]:
    output = _monodis("--assemblyref", path)
    references: dict[str, str] = {}
    current_version: str | None = None
    for line in output.splitlines():
        version_match = re.match(r"\d+: Version=(\S+)", line)
        if version_match:
            current_version = version_match.group(1)
            continue
        name_match = re.match(r"\s*Name=(.+)", line)
        if name_match and current_version:
            references[name_match.group(1).strip()] = current_version
            current_version = None
    return references


def load_inventory(root: Path) -> dict[str, Any]:
    try:
        data = json.loads((root / INVENTORY).read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise ContractError(f"Cannot read dependency inventory: {error}") from error
    validate_inventory(data)
    return data


def validate_inventory(data: dict[str, Any]) -> None:
    if data.get("schema_version") != 1 or data.get("upstream_commit") != UPSTREAM:
        raise ContractError("Inventory schema or upstream commit is not frozen")
    assets = data.get("managed_assets")
    if not isinstance(assets, list) or not assets:
        raise ContractError("Inventory has no managed assets")
    names = [asset.get("assembly_name") for asset in assets]
    if len(names) != len(set(names)):
        raise ContractError("Inventory contains duplicate managed simple names")
    if data.get("allowed_reference_unifications") != EXPECTED_UNIFICATIONS:
        raise ContractError("Inventory reference unifications are not frozen")
    if data.get("reference_unification_evidence") != EXPECTED_UNIFICATION_EVIDENCE:
        raise ContractError("Inventory unification evidence is not frozen")
    if data.get("harmony_blob") != EXPECTED_HARMONY_BLOB:
        raise ContractError("Inventory Harmony blob contract is not frozen")
    for asset in assets:
        required = {
            "path", "package_id", "package_version", "package_sha256", "asset_sha256",
            "assembly_name", "assembly_version", "mvid", "license",
        }
        if required - asset.keys():
            raise ContractError(f"Incomplete inventory entry: {asset.get('path')}")
        if asset["license"] != "MIT":
            raise ContractError(f"Non-MIT asset: {asset['path']}")
        package_key = f"{asset['package_id']}|{asset['package_version']}"
        if EXPECTED_PACKAGE_SHAS.get(package_key) != asset["package_sha256"]:
            raise ContractError(f"Unfrozen package provenance: {package_key}")
        if (
            package_key == "System.Numerics.Vectors|4.4.0"
            and asset.get("package_asset") != NUMERICS_PACKAGE_ASSET
        ):
            raise ContractError("Numerics package asset is not frozen")
    actual_packages = {
        f"{asset['package_id']}|{asset['package_version']}" for asset in assets
    }
    if actual_packages != set(EXPECTED_PACKAGE_SHAS):
        raise ContractError("Inventory NuGet package set is not frozen")


def verify_candidate(root: Path) -> None:
    data = load_inventory(root)
    blob = root / HARMONY_BLOB
    require_sha(blob, HARMONY_SHA256)
    name, version, mvid = assembly_identity(blob)
    if (name, version, mvid) != ("0Harmony", "2.4.2.0", HARMONY_MVID):
        raise ContractError("Harmony blob identity is not frozen")
    if module_name(blob) != "0Harmony":
        raise ContractError("Harmony blob module name is not frozen")
    meta = blob.with_suffix(blob.suffix + ".meta")
    meta_text = meta.read_text(encoding="utf-8")
    if "Editor" not in HARMONY_BLOB.parts:
        raise ContractError("Harmony blob must be physically scoped under Editor")
    if (
        "TextScriptImporter:" not in meta_text
        or "PluginImporter" in meta_text
        or f"guid: {HARMONY_GUID}" not in meta_text
    ):
        raise ContractError("Harmony blob must be an inert Editor-only text asset")

    imported = list((root / "Assets/Plugins").rglob("*.dll"))
    forbidden = [
        path for path in imported
        if path.name == "0Harmony.dll" or path.name.startswith("MonoMod")
        or path.name.startswith("Mono.Cecil")
    ]
    if forbidden:
        raise ContractError(f"Forbidden imported engine/Cecil assets: {forbidden}")

    assets = data["managed_assets"]
    identities = {asset["assembly_name"]: asset for asset in assets}
    actual_paths = {
        path.relative_to(root).as_posix()
        for path in (root / ROSLYN_DIR).glob("*.dll")
    }
    inventoried_paths = {asset["path"] for asset in assets}
    if actual_paths != inventoried_paths:
        raise ContractError("Roslyn closure bytes and inventory paths differ")
    for asset in assets:
        path = root / asset["path"]
        require_sha(path, asset["asset_sha256"])
        actual = assembly_identity(path)
        expected = (asset["assembly_name"], asset["assembly_version"], asset["mvid"])
        if actual != expected:
            raise ContractError(f"{path}: identity {actual}, expected {expected}")
        if path.name != f"{asset['assembly_name']}.dll":
            raise ContractError(f"{path}: filename differs from managed name")
        importer = path.with_suffix(path.suffix + ".meta").read_text(encoding="utf-8")
        required_importer_terms = (
            "PluginImporter:", "isExplicitlyReferenced: 1", "Editor: Editor",
            "UNITY_2021_1_OR_NEWER",
        )
        if any(term not in importer for term in required_importer_terms):
            raise ContractError(f"{path}: importer is not explicit Unity 6 Editor-only")
        for reference, reference_version in assembly_references(path).items():
            if reference in FRAMEWORK_REFERENCES or reference.startswith("Unity"):
                continue
            dependency = identities.get(reference)
            if dependency is None:
                raise ContractError(f"{path}: unresolved dependency {reference} {reference_version}")
            if dependency["assembly_version"] != reference_version:
                edge = f"{asset['assembly_name']}|{reference}|{reference_version}"
                if data["allowed_reference_unifications"].get(edge) == dependency["assembly_version"]:
                    continue
                raise ContractError(
                    f"{path}: {reference} needs {reference_version}, inventory has "
                    f"{dependency['assembly_version']}"
                )

    for asmdef in (
        root / "Assets/Scripts/Editor/FastScriptReload.Editor.asmdef",
        root / "Assets/Scripts/Runtime/FastScriptReload.Runtime.asmdef",
    ):
        if "0Harmony.dll" in asmdef.read_text(encoding="utf-8"):
            raise ContractError(f"{asmdef}: compile-time Harmony reference remains")

    result = subprocess.run(
        ["git", "diff", "--name-only", UPSTREAM, "--", "Assets/Scripts"],
        cwd=root, capture_output=True, text=True, check=True,
    )
    changed_sources = {line for line in result.stdout.splitlines() if line.endswith(".cs")}
    if not changed_sources <= SOURCE_ALLOWLIST or len(changed_sources) > 6:
        raise ContractError(f"Production source budget exceeded: {sorted(changed_sources)}")

    notices = data.get("required_notices")
    if not isinstance(notices, list) or not notices:
        raise ContractError("Inventory has no license notices")
    for notice in notices:
        if not (root / notice).is_file():
            raise ContractError(f"Missing license notice: {notice}")


def validate_loader_source(text: str) -> None:
    required = (
        HARMONY_GUID,
        HARMONY_ASSET_PATH,
        HARMONY_SHA256,
        HARMONY_MVID,
        'new Version(2, 4, 2, 0)',
        '"HarmonyLib.PatchTools"',
        '"DetourMethod"',
        "parameters[0].ParameterType == typeof(MethodBase)",
        "parameters[1].ParameterType == typeof(MethodBase)",
        "Assembly.Load(blobBytes)",
        "AssetDatabase.GUIDToAssetPath(HarmonyBlobGuid)",
        "PackageInfo.FindForAssetPath(assetPath)",
        "TargetInvocationException",
        "ExceptionDispatchInfo.Capture",
    )
    missing = [term for term in required if term not in text]
    if missing:
        raise ContractError(f"Pinned Harmony loader terms missing: {missing}")
    forbidden = ("using HarmonyLib;", "Debug.LogError", "typeof(HarmonyLib.")
    if any(term in text for term in forbidden):
        raise ContractError("Pinned Harmony loader contains false-success dependency use")


def verify_source_candidate(root: Path) -> None:
    polyfills = root / "Assets/Scripts/Runtime/Polyfills.cs"
    validate_loader_source(polyfills.read_text(encoding="utf-8-sig"))

    excluded_mcs = root / "Assets/Scripts/Editor/Compilation/PatchMcsArgsGeneration.cs"
    forbidden_patterns = (
        "using HarmonyLib;", "typeof(HarmonyLib.", "new Harmony(",
        "new HarmonyMethod(", "HarmonyLib.AccessTools",
    )
    for source in (root / "Assets/Scripts").rglob("*.cs"):
        if source == excluded_mcs:
            continue
        source_text = source.read_text(encoding="utf-8-sig")
        if any(pattern in source_text for pattern in forbidden_patterns):
            raise ContractError(f"{source}: compile-time Harmony use remains")

    for config in (root / "Assets").rglob("*.asmdef"):
        if "FastScriptReload_CompileViaMCS" in config.read_text(encoding="utf-8"):
            raise ContractError(f"{config}: unsupported MCS fallback is enabled")
    for config in (root / "Assets").rglob("*.rsp"):
        if "FastScriptReload_CompileViaMCS" in config.read_text(encoding="utf-8"):
            raise ContractError(f"{config}: unsupported MCS fallback is enabled")

    result = subprocess.run(
        ["git", "diff", "--name-only", UPSTREAM, "--", "Assets/Scripts"],
        cwd=root, capture_output=True, text=True, check=True,
    )
    changed_sources = {line for line in result.stdout.splitlines() if line.endswith(".cs")}
    if changed_sources != SOURCE_ALLOWLIST:
        raise ContractError(
            f"Production source delta must equal frozen six files: {sorted(changed_sources)}"
        )
