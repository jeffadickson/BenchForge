# Requirements management

The authoritative baseline is [benchdouble.sdoc](benchdouble.sdoc) (StrictDoc).
[traceability.csv](traceability.csv) tracks status, source, tests, issue and passing
verification evidence. [COVERAGE.md](COVERAGE.md) holds current counts;
[full-audit.csv](full-audit.csv) contains one assessment for every requirement.
The original 90 runner findings remain in [legacy-runner-audit.csv](legacy-runner-audit.csv).

## Every feature change

Update or add a stable requirement ID, an observable acceptance criterion, its
traceability row and GitHub issue in the same commit. Update the full audit row
and coverage report. Preserve deferred items, including procedure/editor work.
Do not turn feature suggestions into implementation-complete claims.

Run `python3 scripts/check_requirements.py` and
`python3 -m unittest discover -s tests/requirements -v` before committing.
Run StrictDoc export separately when installed:
`strictdoc export requirements --output-dir build/requirements`.
Static checks do not replace StrictDoc parsing or runtime acceptance.

## Status and evidence

Planned, DesignOnly and NoImplementationEvidence mean no implemented acceptance
is claimed. PartialSourceUnverified and SourceCommittedUnverified mean source
exists but full behavior is not verified. Blocked identifies an external blocker.
Verified requires a test reference AND a local versioned evidence report containing
tested commit, platform, command, result and requirement mapping. The gate checks
presence, not truth; human review of execution evidence is still necessary.

Each full-audit row fingerprints the baseline statement so changed requirements
cannot silently retain an old audit. Issue existence/state was reviewed against
GitHub during the audit; offline validation checks URL shape, not remote status.
StrictDoc is the requirement tracker, GitHub Issues tracks work, and Reqnroll/xUnit/
Playwright provide executable acceptance evidence. Stable IDs are not renumbered.
