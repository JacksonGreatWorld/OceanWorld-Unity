#!/usr/bin/env python3
"""Restore separately licensed, user-supplied dependencies without downloading.

Default: validate and print a plan. --apply explicitly copies missing files.
Existing unknown bytes, symbolic links, unexpected versions and hand meshes fail.
No Unity process, package manager, network request or license acceptance is run.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys


PROJECT = Path(__file__).resolve().parents[1]
SDK_ID = "com.bytedance.pico.xr"
SDK_VERSION = "6.1.1"
SDK_TARGET = Path("Packages/Local/PICO-Unity-SDK-main/XR")
# The asset source and the runtime package are distinct. Both package versions
# are accepted only when their sample bytes match the original project meshes.
HAND_VERSIONS = {"1.4.1", "1.7.3"}
HANDS = {
    "LeftHand.fbx": {
        "sha256": "828996c1e01f710222a90caa3451a2099ec82f0651242a9b4847091a508ec521",
        "guid": "d7adf6dd117f4261aa0d8644f1fb3755",
    },
    "RightHand.fbx": {
        "sha256": "215bd9c9782a74c95b47e08e802bd2e37b9bd0e2205428543dbdef9da7990db3",
        "guid": "286e6fc04f304fcda525cd657361399a",
    },
}


class RestoreError(Exception):
    pass


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def reject_links(path):
    """Check before resolving, including every ancestor of an input/output."""
    absolute = Path(os.path.abspath(path))
    for node in (absolute, *absolute.parents):
        if node.is_symlink():
            raise RestoreError("Symbolic-link paths are not accepted.")
    return absolute


def package_root(supplied, nested=None):
    # A user-selected cache root may be an alias to a mounted disk. Resolve that
    # explicit selection once; links *inside* the package remain forbidden.
    candidate = Path(supplied).expanduser().resolve(strict=True)
    if not (candidate / "package.json").is_file() and nested:
        candidate = reject_links(candidate / nested)
    if not candidate.is_dir() or not (candidate / "package.json").is_file():
        raise RestoreError("Each input must be a package directory containing package.json.")
    return candidate


def package_identity(directory, expected_name, versions):
    reject_links(directory / "package.json")
    try:
        data = json.loads((directory / "package.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise RestoreError("Cannot read package identity.") from exc
    if data.get("name") != expected_name or data.get("version") not in versions:
        raise RestoreError("Package name/version does not match the pinned dependency.")
    return data["version"]


def destination(relative):
    target = reject_links(PROJECT / relative)
    if PROJECT not in target.parents:
        raise RestoreError("Output escaped the project directory.")
    return target


def sdk_plan(source):
    if not (source / "LICENSE.md").is_file():
        raise RestoreError("The supplied PICO package has no LICENSE.md; use an authentic authorized package.")
    target_root = destination(SDK_TARGET)
    if target_root == source or target_root in source.parents or source in target_root.parents:
        raise RestoreError("Use a separate original SDK source directory, outside the restore target.")
    plan = []
    for base, directories, files in os.walk(source, followlinks=False):
        base = Path(base)
        for name in directories + files:
            reject_links(base / name)
        if any(name in {".git", ".svn", ".hg", "Library", "Temp", "node_modules"} for name in directories):
            raise RestoreError("SDK input contains repository/cache directories; supply only the SDK package.")
        for name in files:
            item = base / name
            if not item.is_file():
                raise RestoreError("SDK input contains a non-regular file.")
            if name == ".DS_Store":
                continue
            relative = item.relative_to(source)
            plan.append((item, SDK_TARGET / relative, sha256(item)))
    if not plan:
        raise RestoreError("SDK package is empty.")
    expected = {relative.relative_to(SDK_TARGET).as_posix() for _, relative, _ in plan}
    if target_root.exists():
        if not target_root.is_dir():
            raise RestoreError("SDK destination is not a directory.")
        for item in target_root.rglob("*"):
            reject_links(item)
            if item.is_file() and item.relative_to(target_root).as_posix() not in expected:
                raise RestoreError("SDK destination contains an unknown extra file; nothing was copied.")
    return plan


def hand_plan(source):
    sample = source / "Samples~" / "HandVisualizer" / "Models"
    plan = []
    metadata = {}
    for name, expected in HANDS.items():
        item = reject_links(sample / name)
        if not item.is_file() or sha256(item) != expected["sha256"]:
            raise RestoreError("XR Hands sample bytes differ from the recorded source meshes.")
        relative = Path("Assets/Resources/DiverHands") / name
        meta = destination(Path(str(relative) + ".meta"))
        if not meta.is_file():
            raise RestoreError("Retained hand .meta is missing; restore it from the public source checkout first.")
        text = meta.read_text(encoding="utf-8")
        match = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", text, flags=re.MULTILINE)
        if not expected["guid"] or not match or match.group(1) != expected["guid"]:
            raise RestoreError("Retained hand GUID differs from the recorded project metadata.")
        metadata[meta] = sha256(meta)
        plan.append((item, relative, expected["sha256"]))
    return plan, metadata


def validate_destinations(plan):
    missing = []
    identical = 0
    for source, relative, digest in plan:
        target = destination(relative)
        if target.exists():
            if not target.is_file() or sha256(target) != digest:
                raise RestoreError("An existing destination has unknown/different bytes; nothing was copied.")
            identical += 1
        else:
            missing.append((source, relative, digest))
    return missing, identical


def copy_missing(plan):
    copied = 0
    for source, relative, digest in plan:
        target = destination(relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        # Exclusive creation also protects against a change after preflight.
        created = False
        try:
            with target.open("xb") as output:
                created = True
                with source.open("rb") as input_stream:
                    shutil.copyfileobj(input_stream, output, 1024 * 1024)
                output.flush()
                os.fsync(output.fileno())
            if sha256(target) != digest:
                raise RestoreError("Copied dependency failed its source hash check.")
        except BaseException:
            if created and target.is_file():
                target.unlink()
            raise
        copied += 1
    return copied


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pico-sdk", type=Path, required=True,
                        help="An already authorized PICO XR SDK 6.1.1 package, or its parent with XR/.")
    parser.add_argument("--xr-hands", type=Path, required=True,
                        help="An already obtained com.unity.xr.hands package with Samples~/HandVisualizer/Models.")
    parser.add_argument("--apply", action="store_true", help="Copy missing files after complete preflight; otherwise only check.")
    args = parser.parse_args(argv)
    if not (PROJECT / "Assets").is_dir() or not (PROJECT / "Packages/manifest.json").is_file():
        raise RestoreError("Run this script from an intact OceanWorld Unity source checkout.")
    sdk = package_root(args.pico_sdk, "XR")
    hands = package_root(args.xr_hands)
    sdk_version = package_identity(sdk, SDK_ID, {SDK_VERSION})
    hand_version = package_identity(hands, "com.unity.xr.hands", HAND_VERSIONS)
    plan = sdk_plan(sdk)
    hand_files, metadata = hand_plan(hands)
    plan.extend(hand_files)
    missing, identical = validate_destinations(plan)
    copied = copy_missing(missing) if args.apply else 0
    if any(not p.is_file() or sha256(p) != digest for p, digest in metadata.items()):
        raise RestoreError("Hand metadata changed during restoration; close Unity and inspect it before continuing.")
    if args.apply:
        remaining, _ = validate_destinations(plan)
        if remaining:
            raise RestoreError("Restoration did not complete.")
    print(json.dumps({"mode": "applied" if args.apply else "check-only", "picoSdkVersion": sdk_version,
                      "handSamplePackageVersion": hand_version, "plannedFiles": len(plan),
                      "alreadyIdenticalFiles": identical, "missingFiles": len(missing),
                      "copiedFiles": copied, "handMetadataPreserved": True}, indent=2))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RestoreError, OSError) as exc:
        print("Dependency restoration stopped: " + str(exc), file=sys.stderr)
        sys.exit(1)
