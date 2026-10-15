# Repository development rules

Every accepted feature or behavior change must update the requirements in the
same commit: stable StrictDoc UID, observable acceptance, canonical traceability,
GitHub work item, full-audit assessment and coverage counts. Reuse an existing
requirement when its scope already covers the change; do not create duplicate
requirements merely to increase counts. Keep deferred work explicitly tracked.

Read requirements/README.md before changing behavior. Run
`python3 scripts/check_requirements.py` and the applicable focused tests.
For tracker changes also run `python3 -m unittest discover -s tests/requirements -v`.
Record commands and limitations honestly. Source or a test name is not proof of
passing acceptance. Verified needs a versioned JSON execution evidence report
with tested_commit (full SHA), platform, command, result=passed and requirements
(list of IDs), plus review of the actual run. Do not fabricate evidence.

Keep canonical lifecycle, ownership and command contracts shared across direct,
generated and remote drivers. Do not implement competing lease authorities.
Default to exclusive ownership; do not infer safe retries or safe physical state
from transport success, cancellation, lease expiration or missing heartbeats.

Routine incremental pushes use [skip ci]; reserve paid hosted runs for explicitly
requested verification or agreed substantial milestones. Prefer local tests.
Procedure/editor expansion remains deferred while lifecycle/recovery foundations
are built, unless the user changes that priority.
