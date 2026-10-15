# PyMeasure remote reliability review

Reviewed 2026-10-15 at upstream commit
`f095b3742691709b75ba12f309498030d7a963cc`.

Sources: [Worker](https://github.com/pymeasure/pymeasure/blob/f095b3742691709b75ba12f309498030d7a963cc/pymeasure/experiment/workers.py),
[Listener](https://github.com/pymeasure/pymeasure/blob/f095b3742691709b75ba12f309498030d7a963cc/pymeasure/experiment/listeners.py),
[Procedure](https://github.com/pymeasure/pymeasure/blob/f095b3742691709b75ba12f309498030d7a963cc/pymeasure/experiment/procedure.py),
[Worker API](https://pymeasure.readthedocs.io/en/latest/api/experiment/workers.html),
[Listener API](https://pymeasure.readthedocs.io/en/latest/api/experiment/listeners.html).

| Question | Finding in inspected path |
|---|---|
| Experiment exceptions? | Worker catches execution exceptions, emits errors and updates failed status. |
| Cleanup? | Worker invokes shutdown in finally; Procedure provides the device-specific shutdown hook. |
| Remote operation? | Worker publishes results/status over optional ZMQ PUB; Listener subscribes with ZMQ SUB. This is telemetry distribution, not the instrument ownership protocol we require. |
| Heartbeat and fenced leases? | No bidirectional health/renewal/ownership-generation mechanism found in these modules. |
| Durable command acceptance/status lookup? | No command ledger with accepted/started/completed receipts found in this path. |
| Shutdown failure isolation? | Worker.shutdown calls procedure.shutdown before stopping recorder and network resources; an exception there can interrupt the later cleanup statements. |
| Safe retries and uncertainty? | No operation-classified replay or crash-window reconciliation found in this path. |

Listener uses cloudpickle deserialization. That protocol should not be copied as
an untrusted network boundary. BenchDouble's protocol should carry versioned,
validated data contracts, authenticated ownership and explicit outcome evidence.

Scope: source inspection of these three upstream modules plus official API pages;
no PyMeasure runtime or hardware tests, and no assertion about all plugins,
adapters or external deployments. The earlier phrase “remote framework” was too
broad for the Worker/Listener facility. We can reuse the conceptual separation
of execution and observation, but must build and verify the stronger ownership,
health and command semantics ourselves. BenchDouble does not yet satisfy them.
