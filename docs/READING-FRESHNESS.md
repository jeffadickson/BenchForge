# Cached reading health

The local IVI status request accepts an optional `readingFreshnessMilliseconds` budget from 1 through 86400000. Existing requests containing only a lease capability remain valid. Every observer can choose its own policy without changing the instrument, another experiment, heartbeat expiry or ownership.

`readingHealth` returns the monotonic age, selected limit and one state:

| State | Meaning |
|---|---|
| Missing | No successful reading exists. |
| Unspecified | A reading exists, but the caller supplied no freshness policy. |
| Fresh | Age is below the caller's limit. |
| Stale | Age equals or exceeds the caller's limit. |

The setup page provides a tab-specific limit and displays owner contact, device state, cached-data age, last device success and last reading receipt independently. The displayed age is a snapshot at refresh. Refreshing status does not measure, reopen, renew or recover an instrument. A healthy heartbeat does not imply a healthy device; fresh data does not certify physical safety. Faults retain prior reading metadata.

No automatic refresh loop or new background worker is created. Concurrent assessments share no mutable policy. Fleet dashboards and real-browser acceptance remain unfinished under BD-HB-006. Earlier transient-policy regressions passed checkpoint 300 of the previous batch; the saved-policy increment passed checkpoint 100 of the new batch.

## Saved experiment policy

Each setup role may save `readingFreshnessMilliseconds`, from 1 through 86400000, or null for unspecified. Import, offline draft save and saved-file read apply the same validation. Saving changes the configuration revision; invalid policies cannot replace the prior saved bytes. Legacy roles omit the property and retain unspecified behavior.

Activation pins the selected role policy and configuration revision before opening the provider. Later saves affect subsequent activations. Status without an override uses the pinned policy; an explicit observer override affects only that request. Cached status exposes both the pinned role limit and the assessed limit. Faults retain the pinned policy and last reading metadata. This adds no new lease, dispatch, renewal or automatic recovery.

The setup page edits limits alongside each role. Blank means unspecified. Invalid edits revert to the prior accepted value. A saved-policy checkbox selects the activation policy by default; uncheck it to use a tab-specific override. Changing this selection requires refresh before a new assessment is displayed. Two-field role construction/deconstruction remains compatible.
