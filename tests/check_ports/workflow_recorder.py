"""Build a small managed child recorder for actual developer-process boundary tests."""
from pathlib import Path
import hashlib
import os
import subprocess

ROOT=Path(__file__).resolve().parents[2]
SOURCE=r'''using System.Text.Json;
var items = args;
File.AppendAllText("events.jsonl", JsonSerializer.Serialize(items)+Environment.NewLine);
if(File.Exists("sleep"))Thread.Sleep(30000);
return items.Length>0 && File.Exists("fail-"+items[0]) ? int.Parse(File.ReadAllText("fail-"+items[0])) : File.Exists("exit-code") ? int.Parse(File.ReadAllText("exit-code")) : 0;
'''
PROJECT='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><UseAppHost>true</UseAppHost></PropertyGroup></Project>'


def recording_host():
    """Build once per source fingerprint and return an executable path containing spaces."""
    directory=ROOT/'.tools/workflow recorder'
    directory.mkdir(parents=True,exist_ok=True)
    executable=directory/'bin'/('WorkflowRecorder.exe' if os.name=='nt' else 'WorkflowRecorder')
    fingerprint=hashlib.sha256((SOURCE+PROJECT).encode()).hexdigest()
    stamp=directory/'source.sha256'
    if not executable.exists() or not stamp.exists() or stamp.read_text()!=fingerprint:
        (directory/'Program.cs').write_text(SOURCE,encoding='utf-8')
        (directory/'WorkflowRecorder.csproj').write_text(PROJECT,encoding='utf-8')
        result=subprocess.run(['dotnet','build',str(directory/'WorkflowRecorder.csproj'),'-c','Release','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-o',str(directory/'bin')],cwd=ROOT,capture_output=True,text=True,timeout=300)
        if result.returncode:raise RuntimeError(result.stdout+result.stderr)
        stamp.write_text(fingerprint)
    return str(executable)
