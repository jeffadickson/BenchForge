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

No automatic refresh loop or new background worker is created. Concurrent assessments share no mutable policy. Fleet dashboards and persisted experiment freshness policies remain unfinished under BD-HB-006. Deterministic tests are executed at checkpoint 300; this source change alone is not acceptance evidence.
