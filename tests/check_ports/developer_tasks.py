"""Exercise compiled developer plans and child execution without building applications."""
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    """Verify argument boundaries, generation ordering, exclusions and failure propagation."""
    shell = 'powershell' if os.name == 'nt' else 'pwsh'
    plan = subprocess.run([shell, '-NoProfile', '-File', str(ROOT / 'build.ps1'), '-PlanOnly'],
                          capture_output=True, text=True, check=True, timeout=60)
    segment = json.loads(plan.stdout.lstrip('\ufeff'))['cache_segment']
    dll = ROOT / '.tools/check-workflow' / segment / 'bin/BenchForge.CheckWorkflow.dll'
    with tempfile.TemporaryDirectory(prefix='developer tasks ', dir=ROOT / 'build') as directory:
        fixture = Path(directory)
        projects = ['src/App/App.csproj', 'examples/Demo/Demo.csproj',
                    'tools/Generator/Generator.csproj', 'generated/Driver/Driver.csproj',
                    'tests/Core/Core.csproj', 'tests/BenchDouble.Browser.Tests/BenchDouble.Browser.Tests.csproj',
                    'src/App/obj/Hidden.csproj']
        for name in projects:
            path = fixture / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.touch()
        recorder = "import sys,json,pathlib\np=pathlib.Path('events.jsonl')\nwith p.open('a') as f:f.write(json.dumps(sys.argv)+'\\n')\nsys.exit(int(pathlib.Path('exit-code').read_text()) if pathlib.Path('exit-code').exists() else 0)\n"
        for name in ['run', 'build', 'test', 'scripts/check_instrument_catalog.py']:
            path = fixture / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(recorder)
        host = ['dotnet', str(dll), '--root', str(fixture), '--python', sys.executable,
                '--dotnet', sys.executable, '--configuration', 'Debug']

        def invoke(*args, expected=0):
            """Run the compiled host and require the original child exit status."""
            result = subprocess.run([*host, *args], capture_output=True, text=True, timeout=60)
            assert result.returncode == expected, (result.stdout, result.stderr)
            return result

        for task, count in [('build', 7), ('test', 4), ('generate', 3)]:
            steps = json.loads(invoke('--command', 'plan', '--task', task).stdout)
            assert len(steps) == count, steps
            assert all(s['Executable'] == sys.executable for s in steps)
            assert not any('Hidden.csproj' in a or 'Browser.Tests.csproj' in a for s in steps for a in s['Arguments'])
            assert not (fixture / 'events.jsonl').exists()
        invoke('--command', 'build')
        events = [json.loads(s) for s in (fixture / 'events.jsonl').read_text().splitlines()]
        assert len(events) == 7 and events[0][0].endswith('check_instrument_catalog.py')
        assert events[1][0] == 'run' and events[2][0] == 'run'
        assert all('Debug' in e for e in events[1:])
        (fixture / 'events.jsonl').unlink()
        (fixture / 'exit-code').write_text('23')
        invoke('--command', 'test', expected=23)
        assert len((fixture / 'events.jsonl').read_text().splitlines()) == 1
        invoke('--command', 'plan', '--task', 'bogus', expected=2)
        print('Developer workflow: plans, argument routing, execution ordering and fail-fast passed.')


if __name__ == '__main__':
    main()
