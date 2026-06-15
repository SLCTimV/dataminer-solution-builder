# Stage 1 — Model Analyzer

Parses a **natural-language description** or an existing **document** (Word, PDF, plain text) and produces a structured YAML domain spec consumed by all downstream stages.

---

## Responsibility

- Extract the primary domain entity, its fields, enums, sub-objects, and cross-model references.
- Map user vocabulary to the canonical YAML property types (see table below).
- Derive all solution naming conventions automatically (never asks the user).
- Write the output to `<WorkspaceRoot>\<DomainName>Input.yaml`.

---

## Input

| Source | Description |
|--------|-------------|
| Natural language prompt | e.g. "A Booking with Title, Start datetime, End datetime, and a Status of Pending / Confirmed / Cancelled" |
| Document file | Word (.docx), PDF, or plain text describing the business domain |

---

## Output

A YAML file at `<WorkspaceRoot>\<DomainName>Input.yaml` conforming to the [canonical schema](../README.md#yaml-spec--canonical-format).

---

## Type Mapping

| User says | YAML `type` |
|-----------|------------|
| string, text, name, description | `string` |
| date, datetime, timestamp | `DateTime` |
| duration, timespan, length | `TimeSpan` |
| number, integer, int, count | `Int64` |
| decimal, double, float, price | `double` |
| boolean, bool, flag, yes/no | `bool` |
| fixed set of values, status, category, type | `enum` — creates an `enums[]` entry named `<DomainName><FieldName>` |
| list of sub-objects, collection, items | adds to `lists[]`, creates a `subObjects[]` entry |
| reference to another model | `type: "ref", ref: "<ModelName>"` |

---

## Reference Implementation

The agent logic is defined in the **DataMiner Business Model Extractor** skill and the **DataMiner Domain Parser** skill:

- `C:\Users\Tim\source\repos\agents\skills\dataminer-domain-parser\SKILL.md`

---

## TODO / Next Steps

- [ ] Define the agent SKILL.md for this folder
- [ ] Add support for analyzing Word/PDF documents (delegate to document-reading sub-agent)
- [ ] Add validation: warn on unknown types, duplicate field names, missing enum references
- [ ] Write example input prompts and their expected YAML outputs as test cases
