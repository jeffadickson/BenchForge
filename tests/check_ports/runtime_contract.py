"""Focused checker entry-point, Make routing and explicit tool-selection regressions."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT=Path(__file__).resolve().parents[2]
SHELL=r'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' if os.name=='nt' else 'pwsh'


def load_tool(name, environment):
    """Import an owned test tool with isolated selection variables and without invoking its main function."""
    clean={key:value for key,value in os.environ.items() if not key.startswith('BENCHFORGE_')}
    with mock.patch.dict(os.environ,{**clean,**environment},clear=True):
        spec=importlib.util.spec_from_file_location('selected_'+name,ROOT/'tests/check_ports'/f'{name}.py')
        module=importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    return module


class RuntimeEntryTests(unittest.TestCase):
    """Exercise real planning entry points and prove rejection occurs before artifact mutation."""

    @classmethod
    def setUpClass(cls):
        """Read the actual host plan without executing the SDK."""
        result=subprocess.run([SHELL,'-NoProfile','-File','scripts/checks.ps1','-PlanOnly','-Dotnet','missing-sdk'],cwd=ROOT,capture_output=True,timeout=120)
        assert result.returncode==0,(result.stdout,result.stderr)
        cls.plan=json.loads(result.stdout.decode('utf-8-sig'))
        cls.foreign=next(arch for arch in ('arm64','x64') if arch!=cls.plan['architecture'])

    def setUp(self):
        """Allocate a contained scratch directory for no-mutation assertions."""
        parent=ROOT/'build/check-ports';parent.mkdir(parents=True,exist_ok=True)
        self.temporary=tempfile.TemporaryDirectory(dir=parent)
        self.folder=Path(self.temporary.name).resolve()
        self.assertTrue(self.folder.is_relative_to(parent.resolve()))

    def tearDown(self):
        """Remove only the verified owned scratch directory."""
        self.temporary.cleanup()

    def invoke(self, script, arguments, success):
        """Run one bounded entry point with an exact expected process result."""
        result=subprocess.run([SHELL,'-NoProfile','-NonInteractive','-File',script,*arguments],cwd=ROOT,capture_output=True,timeout=120)
        self.assertEqual(result.returncode==0,success,(result.stdout,result.stderr))
        return result

    def test_matching_explicit_plan_is_sdk_free(self):
        """An explicit RID and matching architecture produce the host plan without a toolchain."""
        result=self.invoke('scripts/checks.ps1',['-PlanOnly','-Dotnet','missing-sdk','-RuntimeIdentifier',self.plan['rid'],'-Architecture',self.plan['architecture'],'-Cache',str(self.folder/'unused')],True)
        self.assertEqual(json.loads(result.stdout.decode('utf-8-sig')),self.plan)
        self.assertFalse((self.folder/'unused').exists())

    def test_foreign_plan_is_build_only(self):
        """Same-OS foreign architecture planning never claims execution eligibility."""
        result=self.invoke('scripts/checks.ps1',['-PlanOnly','-RuntimeIdentifier',self.plan['platform']+'-'+self.foreign,'-Cache',str(self.folder/'unused')],True)
        self.assertFalse(json.loads(result.stdout.decode('utf-8-sig'))['can_execute'])
        self.assertFalse((self.folder/'unused').exists())

    def test_conflict_rejected_before_cache_creation(self):
        """Conflicting selectors fail without creating an artifact partition."""
        self.invoke('scripts/checks.ps1',['-PlanOnly','-RuntimeIdentifier',self.plan['platform']+'-'+self.foreign,'-Architecture',self.plan['architecture'],'-Cache',str(self.folder/'unused')],False)
        self.assertFalse((self.folder/'unused').exists())

    def test_cross_os_rejected_before_cache_creation(self):
        """A foreign OS cannot be mistaken for the current check host."""
        platform='linux' if self.plan['platform']!='linux' else 'win'
        self.invoke('scripts/checks.ps1',['-PlanOnly','-RuntimeIdentifier',platform+'-x64','-Cache',str(self.folder/'unused')],False)
        self.assertFalse((self.folder/'unused').exists())

    def test_foreign_execution_rejected_before_build(self):
        """An execution request cannot build or run a foreign architecture implicitly."""
        self.invoke('scripts/checks.ps1',['-RuntimeIdentifier',self.plan['platform']+'-'+self.foreign,'-Command','identity','-Cache',str(self.folder/'unused')],False)
        self.assertFalse((self.folder/'unused').exists())

    def test_native_helper_requires_explicit_runtime(self):
        """Optional standalone builds require an explicit RID."""
        self.invoke('scripts/check-runtime.ps1',[],False)

    def test_native_helper_distinguishes_prepare_and_execute(self):
        """The pure native planner permits preparation and rejects foreign execution."""
        args=['-RuntimeIdentifier',self.plan['platform']+'-'+self.foreign]
        result=self.invoke('scripts/check-runtime.ps1',args,True)
        self.assertFalse(json.loads(result.stdout.decode('utf-8-sig'))['can_execute'])
        self.invoke('scripts/check-runtime.ps1',[*args,'-RequireExecution'],False)

    def test_actual_compiled_host_identity_and_cache_reuse(self):
        """Matching RID execution verifies the real .NET architecture using the existing cache."""
        timing=self.folder/'identity.json'
        result=self.invoke('scripts/checks.ps1',['-RuntimeIdentifier',self.plan['rid'],'-Command','identity','-Timing',str(timing)],True)
        lines=[line for line in result.stdout.decode('utf-8-sig').splitlines() if line.startswith('{')]
        self.assertEqual(json.loads(lines[-1])['architecture'],self.plan['architecture'])
        stages=json.loads(timing.read_text(encoding='utf-8-sig'))
        self.assertNotIn('build-workflow',[stage['name'] for stage in stages])


class ToolSelectionTests(unittest.TestCase):
    """Test exact subprocess argument selection without building optional native tools."""

    def test_verifier_honors_host_and_managed_dll(self):
        """The selected host path remains one argument even when it contains spaces."""
        selected={'BENCHFORGE_DOTNET':'chosen host/dotnet','BENCHFORGE_CHECKS_DLL':'chosen tools/checks.dll'}
        tool=load_tool('verify',selected)
        with mock.patch.object(tool.subprocess,'run') as run:
            tool.run('requirements',ROOT)
            self.assertEqual(run.call_args.args[0][:2],[selected['BENCHFORGE_DOTNET'],str(Path(selected['BENCHFORGE_CHECKS_DLL']))])

    def test_verifier_honors_native_selection(self):
        """Native invocation uses the exact explicit executable path."""
        tool=load_tool('verify',{'BENCHFORGE_CHECKS_NATIVE':'chosen native/checks.exe'})
        with mock.patch.object(tool.subprocess,'run') as run:
            tool.run('requirements',ROOT,native=True)
            self.assertEqual(run.call_args.args[0][0],str(Path('chosen native/checks.exe')))

    def test_missing_explicit_tools_fail_before_parity(self):
        """Missing selected tools cannot silently reduce verification scope."""
        for key in ('BENCHFORGE_CHECKS_DLL','BENCHFORGE_CHECKS_NATIVE','BENCHFORGE_ROSLYN_DLL'):
            environment={key:str(ROOT/'build/check-ports/does-not-exist/selected-tool')}
            tool=load_tool('verify',environment)
            with mock.patch.dict(os.environ,environment):
                with self.assertRaises(FileNotFoundError):tool.main()

    def test_benchmark_honors_explicit_paths(self):
        """Microbenchmark candidates use the explicitly selected DLL, native and Roslyn artifacts."""
        env={'BENCHFORGE_DOTNET':'chosen host/dotnet','BENCHFORGE_CHECKS_DLL':'chosen/checks.dll','BENCHFORGE_ROSLYN_DLL':'chosen/roslyn.dll','BENCHFORGE_CHECKS_NATIVE':'chosen/native.exe'}
        tool=load_tool('benchmark',env)
        self.assertEqual(tool.DOTNET,env['BENCHFORGE_DOTNET'])
        self.assertEqual(tool.DLL,Path(env['BENCHFORGE_CHECKS_DLL']))
        self.assertEqual(tool.AUDIT,Path(env['BENCHFORGE_ROSLYN_DLL']))
        self.assertEqual(tool.NATIVE,Path(env['BENCHFORGE_CHECKS_NATIVE']))

    def test_baseline_prepare_uses_selected_host(self):
        """Baseline preparation invokes the selected .NET host without running benchmarks."""
        tool=load_tool('benchmark',{'BENCHFORGE_DOTNET':'chosen host/dotnet'})
        parent=ROOT/'build/check-ports'
        with tempfile.TemporaryDirectory(dir=parent) as temporary:
            tool.ROOT=Path(temporary).resolve();self.assertTrue(tool.ROOT.is_relative_to(parent.resolve()))
            tool.BUILD=tool.ROOT/'build'
            with mock.patch.object(sys,'argv',['benchmark.py','--prepare-baseline','explicit-ref','--prepare-only']),mock.patch.object(tool.subprocess,'check_output',return_value=b'// fixture\n'),mock.patch.object(tool.subprocess,'run') as run:
                tool.main()
                self.assertEqual(run.call_args.args[0][0],'chosen host/dotnet')

    def test_make_cached_targets_always_validate(self):
        """Both optional file targets retain order-only runtime-plan prerequisites."""
        make=(ROOT/'Makefile').read_text(encoding='utf-8')
        for target in ('$(CHECKS_NATIVE_PATH):','$(ROSLYN_DLL):'):
            rule=next(line for line in make.splitlines() if line.startswith(target))
            self.assertIn('| checks-native-plan',rule)

    def test_make_execution_precedes_recursive_builds(self):
        """Parity validates execution and process identity before optional builds."""
        make=(ROOT/'Makefile').read_text(encoding='utf-8');body=make.split('check-ports: checks-execution-plan\n',1)[1].split('\n\n',1)[0]
        self.assertLess(body.index('$(CHECK_HOST) -Command identity'),body.index('$(MAKE) checks-native'))
        self.assertIn('BENCHFORGE_CHECKS_DLL="$(CURDIR)/$(CHECKS_MANAGED_DLL)"',body)
        self.assertIn('-c $(CHECKS_CONFIGURATION) -r $(CHECKS_RID)',body)
        self.assertIn('"$(DOTNET)" build',body)

    def test_make_forwards_optional_runtime_to_compiled_host(self):
        """Normal Make checks retain compiled routing and avoid empty RID arguments."""
        make=(ROOT/'Makefile').read_text(encoding='utf-8')
        self.assertIn('$(if $(strip $(CHECKS_RID)),-RuntimeIdentifier "$(CHECKS_RID)",)',make)
        self.assertIn('CHECKS_MANAGED_DLL := .tools/checks/managed/$(CHECKS_RID)/$(CHECKS_CONFIGURATION)/',make)


if __name__=='__main__':
    unittest.main(verbosity=2)
