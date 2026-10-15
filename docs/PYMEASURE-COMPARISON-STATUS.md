# BenchDouble vs PyMeasure — Evidence-Based Capability Comparison

**Date:** 2026-10-15. **Do not claim feature superiority without implementation and verification evidence.**

| Capability | BenchDouble status | PyMeasure context | Conclusion |
|---|---|---|---|
| Extensive production instrument driver catalog | Keithley 236 partial DC core; 25 PyMeasure entries inventoried, not ported | Broad existing Python instrument driver ecosystem | PyMeasure ahead in established driver coverage |
| Typed UnitsNet quantities | Initial typed C# APIs and plotting adapter committed | Python values/validators and property factories | BenchDouble has a distinctive typed .NET approach |
| Programmable mock behaviors | DI strategy, virtual clock, timed faults committed | Different approach; feature-level comparison needs source review | Potential differentiation, not established overall superiority |
| Multi-instrument virtual physics | Typed signal graph, thermal sensor, experiment composition prototype | Not a primary PyMeasure design goal | Architectural differentiation |
| Remote instrument hosts | Local host prototype, reservations; production auth/leases incomplete | PyMeasure typically uses VISA adapters | Not yet production comparable |
| Experiment execution | Console examples; integrated step editor and signoff incomplete | PyMeasure has experiment/worker/GUI support | PyMeasure has more mature workflow capabilities today |
| Plotting | ScottPlot/Plotly adapters and standalone HTML demos | Python/Matplotlib ecosystem | Both viable, maturity differs |
| Persistence | Local SQLite time-series recorder committed | Experiment results/data support | Need benchmark and end-to-end evidence |
| Automated requirements | StrictDoc baseline, Reqnroll/xUnit/Playwright source | Outside PyMeasure's primary focus | Differentiation in project workflow, not instrument functionality |

**Assessment:** BenchDouble's proposed **architecture** spans additional concepts—typed DI-composed experiments, simulated physical coupling, host discovery, local persistence and formal requirements—but these do not establish that it has surpassed PyMeasure as a functioning instrument-control product. Hardware driver coverage, production reliability, integrated editor, packaging and successful CI evidence are the main gaps.

For a credible comparison, use the same representative bench (DMM, function generator, SMU, chamber), run real device scenarios and record driver completeness, code size, latency, fault handling, reproducibility, installation effort and support across operating systems. See [issue #39](https://github.com/jeffadickson/BenchDouble/issues/39).
