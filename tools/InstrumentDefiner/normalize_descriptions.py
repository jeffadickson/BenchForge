#!/usr/bin/env python3
"""Normalize imported ExternalInstrumentSource descriptions and propose conservative unit metadata.

Never rewrite upstream source or promote unreviewed units. Keep original text,
source references, inference confidence, and review flags in a separate dataset.
"""
import argparse
import csv
import re
from pathlib import Path

QUANTITY_HINTS = (
    (r"\b(frequency|freq)\b", "Frequency", "Hertz"),
    (r"\b(resistance|ohms?)\b", "ElectricResistance", "Ohm"),
    (r"\b(current|amperes?|amps?)\b", "ElectricCurrent", "Ampere"),
    (r"\b(voltage|volts?)\b", "ElectricPotential", "Volt"),
    (r"\b(temperature|celsius)\b", "Temperature", "DegreeCelsius"),
)
PREFIX = re.compile(r"^(?:gets? or sets?|gets?|sets?|returns?|reads?|writes?|the value of|this property)\s+", re.I)


def concise(value):
    """Return a stable, concise description without inventing functionality."""
    value = re.sub(r"\s+", " ", (value or "").strip())
    value = PREFIX.sub("", value)
    value = value.strip(" .;:")
    if not value:
        return ""
    value = value[0].upper() + value[1:]
    if not value.endswith((".", "!", "?")):
        value += "."
    return value


def unit_candidate(name, description):
    """Return a review-only candidate; ambiguous matches have no accepted type."""
    text = (name or "").replace("_", " ") + " " + (description or "")
    matches = [(q, u) for pattern, q, u in QUANTITY_HINTS if re.search(pattern, text, re.I)]
    if len(matches) != 1:
        return "", "", "ambiguous" if matches else "unknown"
    return (*matches[0], "candidate-unreviewed")


def normalize_csv(source, destination):
    """Preserve every source column while adding non-destructive description/unit suggestions."""
    with source.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames is None:
            raise ValueError("CSV requires headers")
        fields = list(reader.fieldnames)
        added = ["originalDescription", "normalizedDescription", "quantityCandidate",
                 "unitCandidate", "unitReviewStatus"]
        if any(field in fields for field in added):
            raise ValueError("Input already contains normalization columns")
        rows = []
        for row in reader:
            original = row.get("description") or row.get("notes") or ""
            name = row.get("name") or row.get("property") or ""
            quantity, unit, status = unit_candidate(name, original)
            rows.append(dict(row, originalDescription=original,
                             normalizedDescription=concise(original),
                             quantityCandidate=quantity, unitCandidate=unit,
                             unitReviewStatus=status))
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        raise FileExistsError("Refusing to overwrite existing normalized data")
    with destination.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields + added)
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)


def main():
    """Run the command-line validation and report results."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    print(f"Normalized {normalize_csv(args.source, args.destination)} rows")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
