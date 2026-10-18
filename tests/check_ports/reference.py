"""Run the unchanged Python implementations against a caller-selected fixture root."""
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = {"requirements": "check_requirements", "scoreboard": "requirements_scoreboard",
           "neutrality": "check_source_neutrality", "compare": "compare_doc_audits",
           "docs": "check_api_docs", "preflight": "check_docs_portable"}


def main():
    """Override fixture locations without altering reference algorithms or outputs."""
    command, root, *args = sys.argv[1:]
    root = Path(root).resolve()
    spec = importlib.util.spec_from_file_location("reference", ROOT / "scripts" / (SCRIPTS[command] + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.ROOT = root
    if command == "requirements":
        module.DOC = root / "requirements/benchdouble.sdoc"
        module.TRACE = root / "requirements/traceability.csv"
        module.AUDIT = root / "requirements/legacy-runner-audit.csv"
    elif command == "docs":
        module.BASELINE = root / "docs/api-documentation-baseline.json"
        collect = module.collect
        module.collect = lambda: collect(root)
    elif command == "preflight":
        args.insert(0, str(root))
    sys.argv = [str(spec.origin), *args]
    return module.main() or 0


if __name__ == "__main__":
    raise SystemExit(main())
