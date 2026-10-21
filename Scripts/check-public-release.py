#!/usr/bin/env python3
"""Check a public Unity source export locally, without Git, network or Unity actions.

Findings contain only relative path, line number and rule identifier. Reports are
optional, must use an absolute location outside the candidate repository, and are
created exclusively so earlier review evidence is never replaced.
"""
from __future__ import annotations

import argparse
import collections
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import sys
from urllib.parse import urlsplit


CHUNK_BYTES = 1024 * 1024
OVERLAP_BYTES = 4096
LFS_HEADER = b"version https://git-lfs.github.com/spec/v1"
HEX_ONLY = re.compile(rb"[0-9a-fA-F \t\r\n]+\Z")
GUID_LINE = re.compile(rb"(?m)^guid:[ \t]*([0-9a-fA-F]{32})[ \t]*\r?$")
TEXT_SUFFIXES = {
    ".asset", ".meta", ".prefab", ".unity", ".mat", ".anim", ".controller",
    ".overridecontroller", ".mask", ".cs", ".h", ".hpp", ".c", ".cpp",
    ".m", ".mm", ".swift", ".shader", ".hlsl", ".cginc", ".compute",
    ".shadergraph", ".shadersubgraph", ".asmdef", ".asmref", ".rsp",
    ".inputactions", ".json", ".yaml", ".yml", ".toml", ".xml", ".plist",
    ".txt", ".md", ".markdown", ".csv", ".tsv", ".html", ".css", ".js",
    ".ts", ".py", ".sh", ".bash", ".zsh", ".gradle", ".properties",
    ".cfg", ".config", ".ini", ".uss", ".uxml", ".gltf", ".obj", ".mtl",
}
ROOT_TEXT_NAMES = {".gitignore", ".gitattributes", ".editorconfig"}
ROOT_DOC = re.compile(r"^(?:readme(?:[._-].*)?|licen[cs]e(?:[._-].*)?|notice(?:[._-].*)?|third[_-]party.*|asset[_-]sources.*)$", re.I)
LICENSE_DOC = re.compile(r"(?:licen[cs]e|notice|credit|third[_-]?party|asset[_-]?source|attribution|ofl|copying)", re.I)
APPROVED_PUBLIC_DOCS = {
    "docs/ASSETS.md", "docs/DEPENDENCIES.md", "docs/VALIDATION.md", "Sources/README.md",
    "Sources/DiverHands/SOURCE.md", "Sources/DiverHands/UNITY-XR-HANDS-LICENSE.md",
    "Sources/MarineExpansion/COMPANIONS.md", "Sources/MarineExpansion/SOURCE.md",
    "Sources/MarketReef/SOURCE.md", "Sources/SchoolFish/README.md",
}
# These reviewed images are owned artwork or historical Editor captures.
# Approving a path alone must not allow a private evidence image to replace it.
APPROVED_PUBLIC_IMAGES = {
    "docs/images/coral-sanctuary-v2.9.8.png": "d3567316cb105ac53dbcc89dc489d30e8d84840150896cb7a29a06a4d84d6cda",
    "docs/images/coral-scale-v2.8.5.png": "f16c1ce3bea02327c3bcefaa33d9f658bb68690119179284c3223e2b3d4b3d4f",
    "docs/images/marine-turtle-v2.8.9.png": "66c45b871ab53fc7c85f5e5d63a03e4b39a4eb40be96e4d1c1f7b61302cb2321",
    "docs/images/ocean-world.png": "af012628286e6fa36a12a5d089fa1669f3f27239137a4fb028e3b41e8032daa9",
    "docs/images/shell-collection.png": "ba45502f661c12b3e09bad4f189c0c3c94660fd0a5e0d3f73b1e6fc3234731fb",
}
FORBIDDEN_SEGMENTS = {
    "spatial", "server", "servers", "validation", "releases", "builds", "library",
    "temp", "logs", "obj", "usersettings", "accelerationsnapshots", "_audit",
    ".ssh", ".aws", ".azure", ".config", "node_modules", "__pycache__",
    "sourceonly", "unusedresources", "playersaves", "savedata", "performance-data",
}
SECRET_SUFFIXES = {".keystore", ".jks", ".p12", ".pfx", ".key", ".pem", ".mobileprovision", ".provisionprofile"}
SDK_BINARY_SUFFIXES = {".dll", ".so", ".aar", ".jar", ".dylib", ".a", ".lib", ".framework", ".xcframework", ".bundle", ".unitypackage", ".zip", ".7z", ".tar", ".tgz", ".gz", ".rar"}
EVIDENCE_SUFFIXES = {".apk", ".aab", ".ipa", ".xcarchive", ".log", ".perfetto-trace", ".trace", ".db", ".sqlite", ".sqlite3", ".dmp"}
SAVE_NAME = re.compile(r"(?:^|/)(?:ocean-(?:campaign|naturalist|achievements|leaderboard|discoveries)(?:\.[^/]*)?|.*(?:keystore|credentials?|private[_-]?key|access[_-]?token|auth[_-]?token)[^/]*)$", re.I)
HAND_MODEL = re.compile(r"(?:^|/)[^/]*hand[^/]*\.(?:fbx|glb|gltf)$", re.I)
AVP_EDITOR = re.compile(r"(?:^|/)editor/[^/]*(?:visionos|avp)[^/]*(?:\.cs|\.meta)$", re.I)
AVP_PLUGIN = re.compile(r"(?:^|/)plugins/(?:ios|visionos|apple)(?:/|\.)|(?:^|/)plugins/[^/]*(?:visionos|avp)|\.(?:mm|swift)(?:\.meta)?$", re.I)

# Match concrete sensitive material, never a bare word such as token or password.
# Escaped regex literals deliberately do not become a finding in this checker.
TEXT_RULES = [
    ("text.internal_git", re.compile(rb"(?i)(?:code|git|sourcehub)\.byted\.org\b|(?:git@|https?://)[^\s\"'<>]*\.git\.internal\b")),
    ("text.corporate_domain", re.compile(rb"(?i)(?<![a-z0-9_-])(?:[a-z0-9_-]+\.)*(?:bytedance\.(?:com|net|cn)|byted\.(?:org|net)|byteimg\.com|bytedtos\.com|feishu\.cn|larksuite\.com)\b")),
    ("text.nonloopback_ipv4", re.compile(rb"(?<![0-9.])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![0-9.])")),
    ("text.runtime_network_endpoint", re.compile(rb"(?i)https?://[a-z0-9][^\s\"'<>\\)]{2,2048}")),
    ("text.private_network_address", re.compile(rb"(?<![0-9.])(?:10\.(?:[0-9]{1,3}\.){2}[0-9]{1,3}|192\.168\.[0-9]{1,3}\.[0-9]{1,3}|172\.(?:1[6-9]|2[0-9]|3[01])\.[0-9]{1,3}\.[0-9]{1,3}|169\.254\.[0-9]{1,3}\.[0-9]{1,3})(?![0-9.])")),
    ("text.device_serial", re.compile(rb"(?i)(?<![a-z0-9])PB[0-9]{3}[a-z0-9]{8,24}(?![a-z0-9])")),
    ("text.local_user_path", re.compile(rb"(?i)(?:/Users/[a-z0-9._-]+(?=/|[\s\"']|$)|[a-z]:[\\/]Users[\\/][a-z0-9._-]+[\\/])")),
    ("text.local_build_volume", re.compile(rb"/Volumes/(?:ORICO|UnitySSD|OceanPico[0-9]+)(?=/|[\s\"']|$)")),
    ("text.private_key_material", re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----")),
    ("text.github_token", re.compile(rb"(?<![a-zA-Z0-9_])(?:gh[pousr]_[a-zA-Z0-9]{20,255}|github_pat_[a-zA-Z0-9_]{30,255})(?![a-zA-Z0-9_])")),
    ("text.signing_identity", re.compile(rb"(?i)Apple (?:Development|Distribution|Developer ID Application):[^\r\n]{3,256}|(?:iPhoneDeveloperTeamID|appleDeveloperTeamID|DEVELOPMENT_TEAM|developmentTeam|teamIdentifier)[ \t]*(?:=|:)[ \t]*[\"']?[A-Z0-9]{10}(?![A-Z0-9])")),
    ("text.nonempty_signing_credential", re.compile(rb"(?i)(?:keystorePass|keyaliasPass|AndroidKeystorePass|AndroidKeyaliasPass|AndroidKeystoreName|AndroidKeyaliasName)[ \t]*(?:=|:)[ \t]*[\"']?[^\s\"'\r\n;#][^\r\n]{1,255}")),
    ("text.assigned_secret", re.compile(rb"(?i)\b(?:api[_-]?key|access[_-]?token|auth(?:orization)?[_-]?token|client[_-]?secret|private[_-]?key|password|passwd)[ \t]*(?:=|:)[ \t]*[\"']([^\"'\r\n]{8,512})[\"']")),
]
PLACEHOLDER = re.compile(rb"(?i)(?:^|[^a-z])(?:example|placeholder|sample|dummy|redacted|changeme|replace[_ -]?me|your[_ -]?(?:key|token|secret|password)|test[_ -]?(?:key|token|secret|password))(?:$|[^a-z])|^\$\{|^<[^>]+>$")
PUBLIC_REFERENCE_HOSTS = {
    "github.com", "githubusercontent.com", "git-lfs.github.com", "unity.com",
    "unity3d.com", "picoxr.com", "pico-interactive.com", "khronos.org",
    "sketchfab.com", "polyhaven.com", "ambientcg.com", "creativecommons.org",
    "kenney.nl", "opengameart.org", "fonts.google.com", "scripts.sil.org",
    "w3.org", "microsoft.com", "apple.com", "developer.android.com",
}
RUNTIME_SOURCE_SUFFIXES = {".cs", ".json", ".xml", ".config", ".cfg", ".ini", ".properties"}
UNITY_FOUR_PART_VERSION = re.compile(rb"[ \t]*(?:XboxOneVersion|metroPackageVersion):[ \t]*[0-9]+(?:\.[0-9]+){3}[ \t]*\r?$")


def public_reference_url(value: bytes) -> bool:
    try:
        hostname = (urlsplit(value.decode("ascii")).hostname or "").lower()
    except (UnicodeError, ValueError):
        return False
    if hostname in {"localhost", "0.0.0.0", "::1"} or hostname.startswith("127."):
        return True
    if hostname in {"example.com", "example.org", "example.net"} or hostname.endswith((".example", ".invalid", ".test")):
        return True
    return any(hostname == host or hostname.endswith("." + host) for host in PUBLIC_REFERENCE_HOSTS)


def allowed_path(relative: str) -> bool:
    parts = relative.split("/")
    if len(parts) == 1:
        return relative in ROOT_TEXT_NAMES or bool(ROOT_DOC.match(relative))
    if parts[0] in {"Assets", "ProjectSettings", "Scripts", "BuildProfiles"}:
        return True
    if relative == "Packages/manifest.json":
        return True
    if relative in APPROVED_PUBLIC_DOCS or relative in APPROVED_PUBLIC_IMAGES:
        return True
    if parts[0] == "docs":
        return bool(LICENSE_DOC.search(relative)) and Path(relative).suffix.lower() in {".md", ".txt", ".json", ".csv"}
    return False


class PublicReleaseCheck:
    def __init__(self, root: Path, private_literals=()):
        self.root = root
        self.private_literals = tuple(literal.encode("utf-8") for literal in private_literals)
        self.findings: set[tuple[str, int, str]] = set()
        self.guids: dict[bytes, list[tuple[str, int]]] = collections.defaultdict(list)
        self.stats = {"filesScanned": 0, "textFilesScanned": 0, "textBytesRead": 0, "hexPayloadBytesSkipped": 0, "lfsPointers": 0}

    def add(self, path: str, line: int, rule: str) -> None:
        self.findings.add((path, max(1, line), rule))

    def check_path(self, relative: str) -> None:
        parts = relative.split("/")
        lowered = [part.lower() for part in parts]
        base = parts[-1]
        unmeta = relative[:-5] if relative.lower().endswith(".meta") else relative
        suffix = Path(unmeta).suffix.lower()
        if not allowed_path(relative):
            self.add(relative, 1, "path.outside_public_whitelist")
        if any((part[:-5] if part.endswith(".meta") else part) in FORBIDDEN_SEGMENTS for part in lowered):
            self.add(relative, 1, "path.private_work_or_runtime_data")
        if any(part.startswith("._") for part in parts) or base in {".DS_Store", "Thumbs.db"}:
            self.add(relative, 1, "path.host_metadata")
        if suffix in SECRET_SUFFIXES or base.startswith(".env") or SAVE_NAME.search(relative):
            self.add(relative, 1, "path.credential_or_player_save")
        if suffix in SDK_BINARY_SUFFIXES or (len(parts) > 1 and parts[0] == "Packages" and relative != "Packages/manifest.json"):
            self.add(relative, 1, "path.sdk_or_unapproved_package_payload")
        if suffix in EVIDENCE_SUFFIXES:
            self.add(relative, 1, "path.build_log_database_or_capture")
        if HAND_MODEL.search(relative) or re.search(r"(?i)^Assets/Resources/Prefabs/Hand(?:Left|Right)\.prefab(?:\.meta)?$", relative):
            self.add(relative, 1, "path.nonredistributable_hand_model")
        if AVP_EDITOR.search(relative) or AVP_PLUGIN.search(relative):
            self.add(relative, 1, "path.avp_specific_plugin_or_editor")
        if re.search(r"(?i)(?:^|/)(?:PICO-Unity-SDK[^/]*|PxrPlatform|LoaderForUnitySDK)(?:/|\.)", relative):
            self.add(relative, 1, "path.vendor_sdk_payload")

    def iter_files(self):
        stack = [self.root]
        while stack:
            folder = stack.pop()
            try:
                entries = sorted(os.scandir(folder), key=lambda e: e.name)
            except OSError:
                self.add(folder.relative_to(self.root).as_posix(), 1, "scan.unreadable_directory")
                continue
            for entry in entries:
                path = Path(entry.path)
                relative = path.relative_to(self.root).as_posix()
                if entry.is_symlink():
                    self.add(relative, 1, "filesystem.symlink_not_a_self_contained_export")
                    continue
                if entry.name == ".git" and folder == self.root:
                    continue  # Local Git metadata is not a candidate source-tree file.
                if entry.is_dir(follow_symlinks=False):
                    stack.append(path)
                elif entry.is_file(follow_symlinks=False):
                    yield path, relative
                else:
                    self.add(relative, 1, "filesystem.nonregular_input")

    def scan_text(self, stream, path: str) -> None:
        tail = b""
        line_at_chunk = 1
        while True:
            chunk = stream.read(CHUNK_BYTES)
            if not chunk:
                return
            self.stats["textBytesRead"] += len(chunk)
            window = tail + chunk
            first_line = line_at_chunk - tail.count(b"\n")
            # Serialized Texture2D assets can contain tens of MB of hexadecimal
            # image data on one line. Hex-only windows cannot contain these ASCII
            # endpoints, paths or secret markers, so avoid repeated regex passes.
            if len(window) > 8192 and HEX_ONLY.fullmatch(window):
                self.stats["hexPayloadBytesSkipped"] += len(chunk)
            else:
                for rule, pattern in TEXT_RULES:
                    for match in pattern.finditer(window):
                        if rule == "text.assigned_secret" and PLACEHOLDER.search(match.group(1)):
                            continue
                        if rule == "text.private_network_address":
                            octets = match.group().split(b".")
                            if any(int(octet) > 255 for octet in octets):
                                continue
                        if rule == "text.nonloopback_ipv4":
                            octets = [int(octet) for octet in match.group().split(b".")]
                            if any(octet > 255 for octet in octets) or octets[0] == 127 or octets in ([0, 0, 0, 0], [255, 255, 255, 255]):
                                continue
                            line_start = window.rfind(b"\n", 0, match.start()) + 1
                            line_end = window.find(b"\n", match.end())
                            if line_end < 0:
                                line_end = len(window)
                            if path == "ProjectSettings/ProjectSettings.asset" and UNITY_FOUR_PART_VERSION.fullmatch(window[line_start:line_end]):
                                continue
                        if rule == "text.runtime_network_endpoint":
                            # Licensing/docs URLs remain allowed. Review literal
                            # service URLs in runtime sources, not their domains
                            # embedded here as another copy of production data.
                            if not path.startswith("Assets/") or "/editor/" in path.lower() or Path(path).suffix.lower() not in RUNTIME_SOURCE_SUFFIXES or public_reference_url(match.group()):
                                continue
                        self.add(path, first_line + window.count(b"\n", 0, match.start()), rule)
                for literal in self.private_literals:
                    offset = 0
                    while (offset := window.find(literal, offset)) >= 0:
                        self.add(path, first_line + window.count(b"\n", 0, offset), "text.private_release_policy")
                        offset += len(literal)
            line_at_chunk += chunk.count(b"\n")
            tail = window[-OVERLAP_BYTES:]

    def scan_file(self, file: Path, relative: str) -> None:
        self.stats["filesScanned"] += 1
        self.check_path(relative)
        try:
            original = file.stat()
            with file.open("rb") as stream:
                prefix = stream.read(4096)
                if relative in APPROVED_PUBLIC_IMAGES:
                    stream.seek(0)
                    digest = hashlib.sha256()
                    while block := stream.read(CHUNK_BYTES):
                        digest.update(block)
                    if digest.hexdigest() != APPROVED_PUBLIC_IMAGES[relative]:
                        self.add(relative, 1, "image.not_the_reviewed_public_image")
                if prefix.lstrip(b"\xef\xbb\xbf").startswith(LFS_HEADER):
                    self.stats["lfsPointers"] += 1
                    self.add(relative, 1, "lfs.unresolved_source_pointer")
                if file.suffix.lower() == ".meta":
                    stream.seek(0)
                    meta = stream.read(131072)
                    for match in GUID_LINE.finditer(meta):
                        self.guids[match.group(1).lower()].append((relative, 1 + meta.count(b"\n", 0, match.start())))
                is_text = file.suffix.lower() in TEXT_SUFFIXES or file.name in ROOT_TEXT_NAMES or bool(ROOT_DOC.match(file.name))
                if is_text and b"\0" not in prefix:
                    self.stats["textFilesScanned"] += 1
                    stream.seek(0)
                    self.scan_text(stream, relative)
            current = file.stat()
            if (original.st_size, original.st_mtime_ns, original.st_ino) != (current.st_size, current.st_mtime_ns, current.st_ino):
                self.add(relative, 1, "scan.input_changed_during_review")
        except (OSError, ValueError):
            self.add(relative, 1, "scan.unreadable_file")

    def run(self) -> dict:
        for path, relative in self.iter_files():
            self.scan_file(path, relative)
        duplicates = 0
        for locations in self.guids.values():
            if len(locations) > 1:
                duplicates += 1
                for path, line in locations:
                    self.add(path, line, "guid.duplicate_meta_identity")
        findings = [{"path": p, "line": line, "rule": rule} for p, line, rule in sorted(self.findings)]
        return {
            "schema": "ocean-public-release-check-v1",
            "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "success": not findings,
            **self.stats,
            "metaGuidCount": len(self.guids),
            "duplicateGuidGroups": duplicates,
            "findingCount": len(findings),
            "findings": findings,
            "scope": "Local source-tree policy scan only. No Git, network, Unity build, tests, device or public-upload operation. Binary media is not a textual-secret scan; provenance, redistribution rights, full Git history and actual public-copy build verification require separate review.",
        }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--report", type=Path, help="Fresh absolute report path outside the public candidate; omitted means no report file.")
    parser.add_argument("--private-policy", type=Path, help="Optional absolute JSON outside the candidate: forbiddenLiterals string array. Values never appear in output.")
    parser.add_argument("--max-output", type=int, default=50, help="Maximum finding rows on stdout; the private JSON report always contains every finding.")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    if not root.is_dir():
        parser.error("--root must be an existing directory")
    if args.max_output < 0:
        parser.error("--max-output cannot be negative")
    report = args.report
    if report is not None:
        if not report.is_absolute():
            parser.error("--report must be absolute")
        report = report.resolve()
        if report == root or root in report.parents:
            parser.error("--report must remain outside the public repository")
        if report.exists():
            parser.error("--report must name a fresh file; previous evidence is preserved")
    private_literals = []
    if args.private_policy is not None:
        policy = args.private_policy
        if not policy.is_absolute() or policy.resolve() == root or root in policy.resolve().parents:
            parser.error("--private-policy must use an absolute path outside the public repository")
        try:
            if policy.stat().st_size > 1024 * 1024:
                parser.error("--private-policy exceeds the bounded configuration size")
            config = json.loads(policy.read_text(encoding="utf-8"))
            private_literals = config["forbiddenLiterals"]
            if not isinstance(private_literals, list) or len(private_literals) > 1000 or any(not isinstance(item, str) or not 4 <= len(item.encode("utf-8")) <= OVERLAP_BYTES for item in private_literals):
                parser.error("--private-policy requires bounded nonempty forbiddenLiterals strings")
        except (OSError, ValueError, KeyError, TypeError):
            parser.error("--private-policy is unreadable or has invalid JSON structure")
    result = PublicReleaseCheck(root, private_literals).run()
    if report is not None:
        report.parent.mkdir(parents=True, exist_ok=True)
        with report.open("x", encoding="utf-8") as stream:
            json.dump(result, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
    for row in result["findings"][:args.max_output]:
        print(json.dumps(row, ensure_ascii=False))
    print(json.dumps({key: result[key] for key in ("success", "filesScanned", "textFilesScanned", "findingCount", "duplicateGuidGroups", "lfsPointers")}, ensure_ascii=False))
    return 0 if result["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
