"""Check public source/history and release strings without printing matched values.

Vendor driver packages are checked separately during release review. Network policy
tests may use synthetic private addresses; public documentation must use TEST-NET.
"""
import argparse
import json
import pathlib
import re
import subprocess
import sys


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
        findings.extend(inspect(args.artifact.name, args.artifact.read_bytes(), artifact=True))
    for name, rule in sorted(set(findings)):
        print(f'BLOCKED: {name}: {rule}')
    if findings:
        print('Public data check failed. Replace real data with synthetic fixtures before publishing.')
        return 1
    print('Public data check passed.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
