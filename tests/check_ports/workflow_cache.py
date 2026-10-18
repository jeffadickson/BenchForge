"""Focused workflow-cache regressions; no application tests or native compilation."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]


def main():
    """Exercise cold build, hit, content invalidation, missing output and safe stale-file cleanup."""
    parent = ROOT / 'build/workflow-cache-tests'
    parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=parent, ignore_cleanup_errors=True) as temp:
        folder = pathlib.Path(temp)
        assert folder.resolve().is_relative_to(parent.resolve())
        source = folder / 'checkout'
        for name in ('tools/BenchForge.CheckWorkflow', 'tools/BenchForge.Checks'):
            for file in (ROOT / name).iterdir():
                if file.is_file():
                    target = source / name / file.name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(file, target)
        for name in ('tools/BenchDouble.DocumentationAudit/RoslynAudit.cs', 'scripts/python_ast_inventory.py', 'global.json'):
            target = source / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, target)
        cache = folder / 'cache'
        timing = folder / 'timing.json'
        command = ['powershell', '-NoLogo', '-NoProfile', '-NonInteractive', '-File', str(ROOT / 'scripts/checks.ps1'), '-Root', str(source), '-Cache', str(cache), '-Timing', str(timing), '-PrepareOnly']

        def prepare(expect_build):
            """Run a fresh bootstrap and require the expected build-cache decision."""
            result = subprocess.run(command, capture_output=True, timeout=600)
            assert result.returncode == 0, (result.stdout, result.stderr)
            stages = json.loads(timing.read_text(encoding='utf-8'))
            assert bool(stages) == expect_build, stages
            return (cache / 'source.sha256').read_text()

        first = prepare(True)
        assert prepare(False) == first
        changed = source / 'tools/BenchForge.Checks/Program.cs'
        stamp = changed.stat()
        changed.write_bytes(changed.read_bytes() + b'\n// Cache invalidation fixture.\n')
        # A timestamp-only cache would incorrectly accept this edit.
        import os
        os.utime(changed, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        second = prepare(True)
        assert second != first
        assert prepare(False) == second
        missing = cache / 'bin/python_ast_inventory.py'
        expected = hashlib.sha256(missing.read_bytes()).hexdigest()
        missing.unlink()
        assert prepare(True) == second
        assert hashlib.sha256(missing.read_bytes()).hexdigest() == expected
        manifest = cache / 'source-files.json'
        paths = json.loads(manifest.read_text())
        stale = cache / 'source/tools/BenchForge.Checks/Old.cs'
        stale.write_text('// obsolete cached source\n')
        paths.append('tools/BenchForge.Checks/Old.cs')
        manifest.write_text(json.dumps(paths))
        (cache / 'source.sha256').write_text('invalidate')
        prepare(True)
        assert not stale.exists()
        sentinel = cache / 'outside.cs'
        sentinel.write_text('preserve this owned test sentinel')
        manifest.write_text(json.dumps(['../outside.cs']))
        (cache / 'source.sha256').write_text('invalidate')
        result = subprocess.run(command, capture_output=True, timeout=60)
        assert result.returncode != 0
        assert sentinel.read_text() == 'preserve this owned test sentinel'
    print('PASS: cold build, cached hit, content-only edit, repeated hit, missing artifact, stale source, manifest containment')


if __name__ == '__main__':
    main()
