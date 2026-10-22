#!/usr/bin/env python3
"""Restore a private HTTPS endpoint locally. Default is preflight; no requests are sent."""
import argparse
import json
import os
from pathlib import Path
import re
import tempfile
import unicodedata
from urllib.parse import urlsplit


def validate_base_url(value):
    if not isinstance(value, str) or not value or value != value.strip() or any(ord(c) < 33 or c.isspace() or unicodedata.category(c) == 'Cc' for c in value) or '\\' in value:
        raise ValueError('baseUrl must be a nonempty HTTPS URL without whitespace or backslashes')
    uri = urlsplit(value)
    if uri.scheme != 'https' or not uri.hostname or uri.username is not None or uri.password is not None or '?' in value or '#' in value:
        raise ValueError('requires HTTPS without user information, query or fragment')
    try:
        if uri.port is not None and not 1 <= uri.port <= 65535:
            raise ValueError('invalid HTTPS port')
    except ValueError:
        raise ValueError('invalid HTTPS port') from None
    if '%' in uri.netloc or not re.fullmatch(r'[A-Za-z0-9._:\[\]-]+', uri.netloc):
        raise ValueError('invalid HTTPS host')
    return value.rstrip('/')


def config_bytes(base_url):
    return (json.dumps({'baseUrl': validate_base_url(base_url)}, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    values = parser.add_mutually_exclusive_group(required=True)
    values.add_argument('--base-url', help='HTTPS API base URL only; never supply an administrator password or key')
    values.add_argument('--base-url-file', type=Path, help='private UTF-8 file containing only the HTTPS URL')
    parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--apply', action='store_true', help='explicitly write missing local configuration; default only checks')
    args = parser.parse_args()
    try:
        policy = args.project / 'Assets/SwanUnderwater/OceanReleaseFeatures.cs'
        if policy.is_file() and re.search(r'LeaderboardsEnabled\s*=>\s*false\s*;', policy.read_text(encoding='utf-8')):
            parser.exit(2, 'Configuration disabled for this release; no endpoint was read or written. Keep PrivateRelease absent. A future online edition requires a separate reviewed implementation.\n')
        value = args.base_url if args.base_url is not None else args.base_url_file.read_text(encoding='utf-8').strip('\r\n')
        body = config_bytes(value)
        project = args.project.resolve(strict=True)
        if not (project / 'Assets').is_dir() or not (project / 'ProjectSettings/ProjectVersion.txt').is_file():
            raise ValueError('target is not the expected Unity project layout')
        private = project / 'Assets/Resources/PrivateRelease'
        output = private / 'OceanLeaderboardConfig.json'
        for ancestor in [project / 'Assets', project / 'Assets/Resources', private, output]:
            if ancestor.is_symlink():
                raise ValueError('private configuration must not traverse symlinks')
        if output.exists():
            if not output.is_file() or output.read_bytes() != body:
                raise ValueError('existing configuration differs; original is preserved; review it manually before replacement')
            print(json.dumps({'mode': 'verified', 'identical': True, 'networkRequested': False, 'endpointLogged': False}))
            return 0
        if args.apply:
            private.mkdir(mode=0o700, parents=True, exist_ok=True)
            # Exclusive publication: never truncate an existing private file, including races.
            with tempfile.NamedTemporaryFile(dir=private, prefix='.config-', delete=False) as temporary:
                pending = Path(temporary.name)
                temporary.write(body)
                temporary.flush()
                os.fsync(temporary.fileno())
            try:
                os.chmod(pending, 0o600)
                os.link(pending, output)
            finally:
                pending.unlink()
        print(json.dumps({'mode': 'applied' if args.apply else 'preflight', 'missing': not args.apply,
                          'networkRequested': False, 'endpointLogged': False}))
        return 0
    except (OSError, UnicodeError, ValueError):
        # Do not include the rejected value or filesystem path in shared diagnostics.
        parser.exit(2, 'Configuration rejected; requires a valid private HTTPS URL, valid Unity target, and no conflicting existing file. No configuration was overwritten.\n')


if __name__ == '__main__':
    raise SystemExit(main())
