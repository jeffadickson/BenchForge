# Definition-driven SCPI mock snapshots

Parameter and quantity identities are limited to 128 characters, units to 80;
control characters are rejected. Existing channel (1–256), parameter (1–512),
command/template (4096 characters) and finite-value bounds remain enforced.

Queries and setters must match exactly one declared template. A literal/channel
pattern overlap now fails before changing readings or accepted-command history,
rather than selecting the first parameter. Unsupported commands are still rejected.

`GetTranscriptSnapshot()` returns a read-only detached history copy.
`GetReadingsSnapshot()` returns read-only immutable parameter/channel records with
canonical quantity, unit and finite value, in declaration/channel order. Both use
the existing mock transaction lock and perform no hardware I/O.

`ResetAsync(token)` restores all declared defaults and clears history atomically.
`ClearTranscriptAsync(token)` clears history alone. Observed cancellation before
mutation leaves state unchanged. These simulated controls make no physical safety
or ownership-release claim. The legacy mutable `Transcript` list is retained for
compatibility; arbitrary external mutations of that list are not synchronized.
Use the snapshot and clear APIs for concurrent callers.

Focused regressions are in `ScpiBatch200Tests`, including ambiguous patterns,
metadata boundaries, detached/read-only snapshots, cancellation, parallel writes
and resets, and rejection without state changes. See the 200-iteration ledger and
versioned checkpoint evidence for actual execution results.
