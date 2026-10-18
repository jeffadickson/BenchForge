"""Time the complete check workflow, including a fresh shell and builds, not application tests."""
import argparse
import ctypes
import datetime
import struct
import hashlib
import json
import os
import pathlib
import platform
import statistics
import subprocess
import sys
import time
import types
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = 'b296bb852699e8d526179a3290312acafcb99bda'
BUILD = ROOT / 'build/workflow-benchmark'
REPORTS = ['api-audit.json','requirements-scoreboard.html','source-neutrality-audit.json','docs-preflight.json','documentation-audit.json','docs-parity.json']


def prepare(cache):
    """Create source-only build inputs; do not clear global SDK, package or operating-system caches."""
    source=cache/'source'
    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE],cwd=ROOT,text=True).splitlines()
    for name in paths:
        if name.startswith(('tools/BenchForge.Checks/','tools/BenchDouble.DocumentationAudit/')) or name in ('scripts/python_ast_inventory.py','global.json','tests/requirements/test_gate.py','tests/requirements/test_scoreboard.py'):
            target=source/name
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(subprocess.check_output(['git','show',BASE+':'+name],cwd=ROOT))


def memory():
    """Read current Windows RAM/pagefile availability without launching another shell."""
    if os.name!='nt':return None
    buffer=ctypes.create_string_buffer(64)
    ctypes.c_uint32.from_buffer(buffer).value=64
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(buffer)):return None
    values=struct.unpack_from('<IIQQQQQQQ',buffer)
    return {'load_percent':values[1],'physical_total_bytes':values[2],'physical_available_bytes':values[3],'commit_total_bytes':values[4],'commit_available_bytes':values[5]}


def execute(variant, cache, state):
    """Measure wall time from before shell creation through report writing and focused checks."""
    timing=cache/(state+'-stages.json')
    script=ROOT/('tests/check_ports/baseline-workflow.ps1' if variant=='baseline' else 'scripts/checks.ps1')
    command=['powershell','-NoLogo','-NoProfile','-NonInteractive','-File',str(script),'-Root',str(ROOT),'-Cache',str(cache),'-Timing',str(timing),'-Python',sys.executable]
    memory_before=memory()
    started_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()
    started=time.perf_counter_ns()
    result=subprocess.run(command,cwd=ROOT,capture_output=True,timeout=1200)
    elapsed=(time.perf_counter_ns()-started)/1e6
    (cache/(state+'-stdout.log')).write_bytes(result.stdout)
    (cache/(state+'-stderr.log')).write_bytes(result.stderr)
    assert result.returncode==0,(command,result.stdout.decode(errors='replace'),result.stderr.decode(errors='replace'))
    stages=json.loads(timing.read_text(encoding='utf-8-sig'))
    if isinstance(stages,dict): stages=[stages]
    details=pathlib.Path(str(timing)+'.checks.json')
    checks=json.loads(details.read_text(encoding='utf-8')) if details.exists() else None
    hashes={name:hashlib.sha256((ROOT/'build'/name).read_bytes()).hexdigest() for name in REPORTS}
    return {'variant':variant,'state':state,'started_utc':started_utc,'memory_before':memory_before,'memory_after':memory(),'milliseconds':elapsed,'stages':stages,'checks':checks,'report_sha256':hashes,'shell_overhead_and_unattributed_ms':elapsed-sum(s['milliseconds'] for s in stages)}


def main():
    """Save complete-workflow cold-artifact and cached measurements with stage profiles."""
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--label',default='full')
    parser.add_argument('--cached-runs',type=int,default=3)
    parser.add_argument('--legacy-tracker',type=pathlib.Path)
    args=parser.parse_args()
    if args.legacy_tracker:
        suite=unittest.TestSuite()
        for name in ('test_gate.py','test_scoreboard.py'):
            path=args.legacy_tracker.parent/name
            module=types.ModuleType(path.stem)
            module.__file__=str(ROOT/'tests/requirements'/name)
            sys.modules[path.stem]=module
            exec(compile(path.read_text(encoding='utf-8'),module.__file__,'exec'),module.__dict__)
            suite.addTests(unittest.defaultTestLoader.loadTestsFromModule(module))
        raise SystemExit(not unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful())
    folder=BUILD/args.label
    assert not folder.exists(),'Use a new label for empty per-variant build-artifact caches'
    folder.mkdir(parents=True)
    runs=[]
    expected=None
    for variant in ['baseline','optimized']:
        cache=folder/variant
        cache.mkdir()
        if variant=='baseline':prepare(cache)
        for state in ['cold']+[f'cached-{n}' for n in range(1,args.cached_runs+1)]:
            run=execute(variant,cache,state);runs.append(run)
            if expected is None:expected=run['report_sha256']
            assert expected==run['report_sha256'],(variant,state,'output reports differ')
            print(variant,state,round(run['milliseconds']/1000,3),'seconds',flush=True)
            for stage in run['stages']:print(' ',stage['name'],round(stage['milliseconds']/1000,3),flush=True)
    report={'platform':platform.platform(),'base_revision':BASE,'definition':'Cold means empty per-run build/output directories. Installed SDK, NuGet package cache and OS filesystem cache are retained. Cached means a fresh-shell repeat using the built tools. Includes seven checks/reports and 19 tracker/documentation regression tests, excludes application tests and StrictDoc/Sphinx/DocFX exports. One cold sample per variant; raw cached samples retained. Source copy/setup outside timing, builds and shell startup inside. Variants run sequentially, with no concurrent benchmarks.','runs':runs}
    (folder/'results.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    main()
