# Example mock-only Starlark laboratory procedure (powered by starlark-cs)
instrument("Meter1")
configure("Meter1", "RESISTANCE")

r1 = measure("Meter1", "RESISTANCE", "R1")
verify_between(r1, 95, 105, "Ohm")

wait_s(5)

r2 = measure("Meter1", "RESISTANCE", "R2")
verify_between(r2, 100, 110, "Ohm")
