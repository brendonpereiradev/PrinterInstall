"""Check public source/history and release strings without printing matched values.

Vendor driver packages are checked separately during release review. Network policy
tests may use synthetic private addresses; public documentation must use TEST-NET.
"""
import argparse
import io
import json
import pathlib
import re
import subprocess
import struct
import sys
import zlib


def valid_cpf(value):
    digits = re.sub(r'\D', '', value)
    if len(digits) != 11 or len(set(digits)) == 1:
        return False
    return all((sum(int(digits[i]) * (end + 1 - i) for i in range(end)) * 10 % 11) % 10
               == int(digits[end]) for end in (9, 10))


RULES = {
    'internal-domain': re.compile(r'\b[\w-]+(?:\.[\w-]+)*\.(?:local|corp|internal|lan|intranet)(?![\w.-])', re.I),
    'personal-profile-path': re.compile(r'[A-Za-z]:\\+Users\\+[^\\\s"\'<>]+', re.I),
    'cpf': re.compile(r'(?<!\d)\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?!\d)'),
    'private-address-in-documentation': re.compile(r'(?<![\d.])(?:10|192\.168|172\.(?:1[6-9]|2\d|3[01]))(?:\.\d{1,3}){2,3}(?![\d.])'),
}


def strings(data):
    if data.startswith((b'\xff\xfe', b'\xfe\xff')):
        return data.decode('utf-16', errors='replace')
    if b'\0' not in data[:8192]:
        return data.decode('utf-8', errors='replace')
    ascii_strings = (m.group().decode('ascii') for m in re.finditer(rb'[\x20-\x7e]{6,}', data))
    wide_strings = (m.group().decode('utf-16le') for m in re.finditer(rb'(?:[\x20-\x7e]\x00){6,}', data))
    return '\n'.join((*ascii_strings, *wide_strings))


def inspect(name, data, artifact=False):
    content = strings(data)
    for rule, pattern in RULES.items():
        if rule == 'private-address-in-documentation' and (artifact or name.startswith('tests/')):
            continue
        if any(rule != 'cpf' or valid_cpf(m.group()) for m in pattern.finditer(content)):
            yield name, rule


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args])


def vendor_package(name):
    return name.startswith('drivers/') and not name.startswith('drivers/Gainscha/label-presets/')


def bundle_application_files(path):
    """Read .NET's bundle manifest and inspect the application's own assemblies.

    Layout: dotnet/runtime Microsoft.NET.HostModel/Bundle/{Manifest,FileEntry}.cs.
    Dependency metadata can contain numbers and namespace suffixes resembling
    personal identifiers or internal domains, so it is reviewed separately.
    """
    data = path.read_bytes()
    marker = bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')
    marker_offset = data.find(marker)
    if marker_offset < 8:
        raise ValueError('Executable is not a recognized .NET single-file bundle')
    header_offset = struct.unpack_from('<q', data, marker_offset - 8)[0]
    if not 0 < header_offset < len(data):
        raise ValueError('Invalid bundle header offset')
    reader = io.BytesIO(data)
    reader.seek(header_offset)

    def read_string():
        length = shift = 0
        for _ in range(5):
            part = reader.read(1)
            if len(part) != 1:
                raise ValueError('Truncated bundle manifest')
            byte = part[0]
            length |= (byte & 127) << shift
            if byte < 128:
                return reader.read(length).decode('utf-8')
            shift += 7
        raise ValueError('Invalid bundle string length')

    major, minor, count = struct.unpack('<IIi', reader.read(12))
    if major not in (1, 2, 6) or minor != 0 or not 0 < count < 10000:
        raise ValueError('Unsupported bundle manifest')
    read_string()
    if major >= 2:
        reader.seek(40, io.SEEK_CUR)
    required = {'PrinterInstall.App.dll', 'PrinterInstall.Core.dll'}
    found = set()
    for _ in range(count):
        offset, size = struct.unpack('<qq', reader.read(16))
        compressed = struct.unpack('<q', reader.read(8))[0] if major >= 6 else 0
        file_type = reader.read(1)[0]
        name = read_string()
        if name in required or file_type in (3, 4):
            length = compressed or size
            if offset < 0 or length < 0 or offset + length > header_offset:
                raise ValueError('Invalid bundled file bounds')
            payload = data[offset:offset + length]
            if compressed:
                payload = zlib.decompress(payload, -15)
            if len(payload) != size:
                raise ValueError('Invalid bundled file size')
            found.add(name)
            yield name, payload
    if not required.issubset(found):
        raise ValueError('Application assemblies are missing from bundle')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--history', action='store_true', help='Scan all reachable Git references')
    parser.add_argument('--artifact', type=pathlib.Path, help='Scan a published executable as well')
    args = parser.parse_args()
    root = pathlib.Path(git(pathlib.Path.cwd(), 'rev-parse', '--show-toplevel').decode().strip())
    findings = []
    if args.history:
        objects = {}
        for record in git(root, 'rev-list', '--objects', '--all').decode('utf-8', 'replace').splitlines():
            oid, _, name = record.partition(' ')
            if name and not vendor_package(name):
                objects[oid] = name
        proc = subprocess.Popen(['git', '-C', str(root), 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        for oid, name in objects.items():
            proc.stdin.write((oid + '\n').encode()); proc.stdin.flush()
            header = proc.stdout.readline().split()
            data = proc.stdout.read(int(header[2])); proc.stdout.read(1)
            if header[1] == b'blob':
                findings.extend(inspect(name, data))
        proc.stdin.close(); proc.wait()
        findings.extend(inspect('[commit messages]', git(root, 'log', '--all', '--format=%B')))
    else:
        for name in git(root, 'ls-files', '-z').decode().split('\0'):
            if name and not vendor_package(name) and (root / name).is_file():
                findings.extend(inspect(name, (root / name).read_bytes()))
        config = json.loads((root / 'src/PrinterInstall.App/appsettings.json').read_text(encoding='utf-8-sig'))
        if config.get('DomainName', '').strip() or config.get('LdapHost', '').strip():
            findings.append(('src/PrinterInstall.App/appsettings.json', 'embedded-environment-configuration'))
    if args.artifact:
        try:
            for name, payload in bundle_application_files(args.artifact):
                findings.extend(inspect(f'{args.artifact.name}/{name}', payload, artifact=True))
        except (ValueError, struct.error, zlib.error, IndexError) as error:
            print(f'BLOCKED: {args.artifact.name}: {error}')
            return 1
    for name, rule in sorted(set(findings)):
        print(f'BLOCKED: {name}: {rule}')
    if findings:
        print('Public data check failed. Replace real data with synthetic fixtures before publishing.')
        return 1
    print('Public data check passed.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
