# Integrated Procedure Editor and Test-Lab Runner

## Product requirement

The primary UI is a **structured electronic test procedure editor and runner**, not a notebook or text editor. Users compose, insert, revise, execute, redline, review, and sign off steps in a single connected workspace. Monaco/Roslyn is an optional advanced-code editor for custom actions, not the governing procedure model.

## Separate records

- **Procedure definition:** stable identifier, controlled revision, author, approvals, typed hierarchical steps, dependencies, parameters, instrument requirements, acceptance criteria, mandatory signatures.
- **Execution instance:** immutable snapshot of approved procedure revision, operators, step-specific outcomes, timestamps, measurements, logs, instrument/driver IDs, approvals and deviations.
- **Redline:** proposed deviation scoped to execution, before/after content, reason, initiator, reviewer, authorization, time. Never silently update released procedure.
- **Deviation / nonconformance:** traceable finding with disposition and optional corrective work.
- **Signoff:** attributable electronic signature linked to immutable content hash and record version; role, explicit intent, timestamps and audit evidence; no automatic claim of regulatory compliance.

## Step types

Narrative/instruction, instrument configure, measurement with limits, calculation, wait/condition, branch/loop, operator prompt, safety checkpoint, inspection, attachment/photo, external script/C# action, signature, hold point and subprocedure reference. Every step has a stable ID independent of display numbering; insertion and reordering preserve references.

## Core UX

- One hierarchical procedure view with inline editing when authorized and inline execution controls when running.
- Insert-before/after, drag reorder, duplicate, indent/outdent, parameter forms, conditional steps, templates and subprocedures.
- Show current measured value, pass/fail limits, real-time instrument logs, exceptions and run progress next to step.
- Separate edit draft, released revision, redline proposal and authorized run-time deviation states visually and in data.
- Review redlines with inline text/parameter diffs and approve/reject workflow; approval scoped to exactly specified content.
- Signoff at configurable step/section/run level; signing freezes referenced record content and later edits require new version/attestation.
- Minimal clicks for normal operator flow; all destructive or unsafe actions require appropriate confirmation and authorization.
- Hardware reservation and cleanup are guaranteed by the runner—not arbitrary UI code.
- Support offline-local recording and later synchronization with immutable IDs, conflict detection and explicit reconciliation.

## Security and audit
Role-based permissions (author, operator, reviewer, approver, administrator), host-permission checks, identity-bound signatures, event-sourced audit log, trustworthy clocks and content hashes. Separate safety interlock from a software-signoff check. Standards such as 21 CFR Part 11 require dedicated compliance review and testing before making claims.

## Relationship to architecture
Host services arbitrate instrument access and stream logs; experiment runner executes instrument and calculation steps; procedure engine controls authoring/revisions/redlines/signoff; web UI is a client of versioned APIs. The procedure service may later be a separate application or integrate with TestForge. Do not conflate TestForge's formal qualification governance with the lower-level measurement runner.

## Initial vertical slice
An executable six-step DMM/PSU procedure with editor insert/reorder, simulation-backed execution, limits, one redline, reviewer approval, step signoff, final approval, trace-linked log stream, and immutable archived execution record.

## Status
Approved requirements/design only. No complete integrated editing/signature UI implemented.
