"""Build the Python tooling reference without executing imported instrument drivers."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
project = "BenchDouble Python tools"
extensions = ["sphinx.ext.autodoc"]
html_theme = "alabaster"
autodoc_member_order = "bysource"
exclude_patterns = ["_build"]
