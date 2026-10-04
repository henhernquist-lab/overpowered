#!/usr/bin/env python3
"""Run self-exiting Unity verifiers in this checkout, with durable fresh evidence.

Example: python3 tools/verify-sequential.py --out Verification/Recovery/gates \
    RosterVerification.Run RosterVerification.Reload MeleeVerification.Run
Stops on the first failed process/check; never consumes an old Reload pointer
unless its matching Run passed in this invocation. No project copies or source edits.
"""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True)
    parser.add_argument('--timeout', type=int, default=3000)
    parser.add_argument('entries', nargs='+')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    out = (root / args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    unity = Path('/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity')
    save = Path.home() / 'Library/Application Support/Hen Hernquist Lab/Overpowered Prototype/overpowered-progression.json'
    database = root / 'Assets/Synty/SidekickCharacters/Database/Side_Kick_Data.db'

    def stamp(path):
        return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None

    def git(*argv):
        return subprocess.check_output(['git', *argv], cwd=root, text=True).strip()

    records, passed = [], set()
    for entry in args.entries:
        if not re.fullmatch(r'[A-Za-z0-9_]+\.[A-Za-z0-9_]+', entry):
            parser.error('Expected an Editor Class.Method entry: ' + entry)
        if entry.endswith('.Reload') and entry.removesuffix('.Reload') + '.Run' not in passed:
            parser.error('Reload requires a successful Run in this invocation: ' + entry)
        processes = subprocess.run(['pgrep', '-fl', str(unity)], capture_output=True, text=True)
        if processes.returncode != 1:
            raise RuntimeError('Cannot establish an idle Editor: ' + processes.stdout + processes.stderr)
        log = out / (entry + '.log')
        if log.exists():
            raise RuntimeError('Evidence already exists; choose a new --out directory: ' + str(log))
        record = dict(entry=entry, commit=git('rev-parse', 'HEAD'),
                      status_before=git('status', '--short'),
                      started_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      save_before=stamp(save), database_before=stamp(database), log=str(log.relative_to(root)))
        print('START ' + entry, flush=True)
        started = time.monotonic()
        with subprocess.Popen([str(unity), '-batchmode', '-projectPath', str(root),
                               '-executeMethod', entry, '-logFile', str(log)], cwd=root) as process:
            record['pid'] = process.pid
            try:
                record['exit'] = process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
                record['exit'] = 'TIMEOUT'
        record['seconds'] = round(time.monotonic() - started, 2)
        lines = log.read_text(errors='replace').splitlines() if log.exists() else []
        checks = [line for line in lines if re.match(r'^(?:\[[^\]]+\]\s*)?(?:PASS|FAIL)\b', line)]
        record['pass'] = sum(bool(re.match(r'^(?:\[[^\]]+\]\s*)?PASS\b', line)) for line in checks)
        record['fail'] = len(checks) - record['pass']
        record['save_unchanged'] = record['save_before'] == stamp(save)
        record['database_unchanged'] = record['database_before'] == stamp(database)
        record['status_after'] = git('status', '--short')
        (out / (entry + '.checks.txt')).write_text('\n'.join(checks) + '\n')
        records.append(record)
        (out / 'manifest.json').write_text(json.dumps(records, indent=2) + '\n')
        print(f"END {entry}: exit {record['exit']}, {record['pass']} PASS, {record['fail']} FAIL, {record['seconds']}s", flush=True)
        if record['exit'] != 0 or record['fail'] or not record['pass'] or not record['save_unchanged'] or not record['database_unchanged']:
            print('STOP: inspect manifest and log; no later suite was launched.', flush=True)
            return 1
        passed.add(entry)
    return 0


if __name__ == '__main__':
    sys.exit(main())
