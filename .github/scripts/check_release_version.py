"""Reject release numbers that Windows Installer cannot safely order."""
import re
import subprocess
import sys


def validate_release(requested, tags):
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', requested):
        raise ValueError(f'Invalid release version: {requested}')
    version = tuple(map(int, requested.split('.')))
    if any(value > limit for value, limit in zip(version, (255, 255, 65535))):
        raise ValueError(f'Invalid release version: {requested} exceeds MSI limits')
    previous = [tuple(map(int, tag[1:].split('.'))) for tag in tags
                if re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+', tag)]
    if previous and version <= max(previous):
        raise ValueError(f'Release {requested} must exceed existing release {".".join(map(str, max(previous)))}')


if __name__ == '__main__':
    tags = subprocess.check_output(['git', 'tag', '--list'], text=True).splitlines()
    validate_release(sys.argv[1], tags)
    print(f'PASS: new release {sys.argv[1]}')
