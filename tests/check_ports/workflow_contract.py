"""Check compiled workflow output parity, CLI failures and Make build contracts."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
REPORTS = ('api-audit.json', 'requirements-scoreboard.html', 'source-neutrality-audit.json',
           'docs-preflight.json', 'documentation-audit.json', 'docs-parity.json')


def execute(arguments, expected=0):
    """Capture a bounded child and preserve diagnostics on unexpected failure."""
    result = subprocess.run(arguments, cwd=ROOT, capture_output=True, timeout=300,
                            env={**os.environ, 'PYTHONIOENCODING': 'utf-8'})
    assert result.returncode == expected, (arguments, result.returncode, result.stdout, result.stderr)
    return result


def hashes():
    """Hash the six externally visible reports without normalizing their bytes."""
    return {name: hashlib.sha256((ROOT / 'build' / name).read_bytes()).hexdigest() for name in REPORTS}


def main():
    """Compare all reports with independent Python references and validate Make routing."""
    plan = json.loads(execute(['powershell', '-NoProfile', '-File', 'scripts/checks.ps1', '-PlanOnly']).stdout.decode('utf-8-sig'))
    dll = ROOT / '.tools/check-workflow' / plan['cache_segment'] / 'bin/BenchForge.CheckWorkflow.dll'
    host = ['dotnet', str(dll)]
    identity = json.loads(execute([*host, '--command', 'identity']).stdout)
    assert identity['architecture'] == plan['architecture']
    make = (ROOT / 'Makefile').read_text(encoding='utf-8')
    for target, command in [('scoreboard','scoreboard'), ('requirements-check','requirements'),
                            ('source-audit','neutrality'), ('source-audit-strict','neutrality-strict'),
                            ('docs-preflight','preflight'), ('docs-compare','compare')]:
        body = make.split(target + ':\n', 1)[1].split('\n\n', 1)[0]
        assert '$(CHECK_HOST) -Command ' + command in body, (target, body)
    assert '-r $(CHECKS_RID)' in make and '/$(CHECKS_RID)/$(CHECKS_CONFIGURATION)' in make
    execute([*host, '--command', 'roslyn'])
    reference = [sys.executable, 'tests/check_ports/reference.py']
    for command, extra in [('requirements', []), ('scoreboard', []),
                           ('neutrality', ['--strict', '--output', 'build/source-neutrality-audit.json']),
                           ('preflight', ['--output', 'build/docs-preflight.json']), ('docs', []),
                           ('compare', ['--roslyn','build/api-audit.json','--portable','build/docs-preflight.json','--output','build/docs-parity.json'])]:
        execute([*reference, command, str(ROOT), *extra])
    original = hashes()
    for jobs in (1, 8):
        timing = f'build/check-ports/workflow-{jobs}.json'
        execute([*host, '--command','checks-only','--jobs',str(jobs),'--timing',timing,'--python',sys.executable])
        assert hashes() == original, f'Report bytes changed with jobs={jobs}'
        report = json.loads((ROOT / timing).read_text())
        assert len(report['stages']) == 7 and all(stage['exit_code'] == 0 for stage in report['stages'])
        assert report['architecture'] == plan['architecture']
    result = {'result':'passed','reports':original,'jobs':[1,8], 'architecture':plan['architecture'],
              'make_validation':'source contract only; GNU Make is not installed locally'}
    (ROOT / 'build/check-ports/workflow-contract.json').write_text(json.dumps(result, indent=2)+'\n',encoding='utf-8')
    print('PASS: compiled identity, Make routing, seven stages, six exact report hashes at jobs=1 and jobs=8')


if __name__ == '__main__':
    main()
