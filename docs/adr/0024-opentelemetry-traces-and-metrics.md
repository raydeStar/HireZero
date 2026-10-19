# 0024: OpenTelemetry traces and metrics

Status: Accepted (Phase 3).

## Context

An operator running HireZero for several companies needs to see:
- what each employee is doing and how long its jobs take;
- which tools are called and how often they are refused or fail;
- how many tokens each company spends.

The audit log ([ADR 0022](0022-tamper-evident-audit-log.md)) is the record, but it is meant for review, not
dashboards or alerts. OpenTelemetry is the vendor-neutral way to feed whatever backend a deployment already has.

## Decision

- **Core emits through the .NET runtime only.** `CompanyTelemetry` holds one `ActivitySource` and one `Meter`,
  both named `HireZero.Company`. Core takes no OpenTelemetry package, and no role or integration emits directly.
- **Traces:**
  - `employee.job`, for each claimed scheduler job;
  - `tool <id>`, for each tool call;
  - HTTP server spans from ASP.NET Core, tagged with the company the request acts in.
- **Metrics:**

  | Metric | Unit | Tags |
  | --- | --- | --- |
  | `hirezero.jobs` | job | company, employee, outcome |
  | `hirezero.job.duration` | s | company, employee, outcome |
  | `hirezero.tool.calls` | call | company, employee, tool, risk, outcome |
  | `hirezero.model.calls` | call | company, employee, phase |
  | `hirezero.model.tokens` | token | company, employee |
  | `hirezero.coding.runs` | run | company, employee, state |

  Tokens are counted as provider usage is first reported for an attempt, which includes coding runs. They are the
  same tokens the budgets charge.
- **Only what is committed is counted.** Measurements that come from a database change are emitted after its
  transaction commits, so a rolled-back change counts nothing.
- **Tags are identifiers only.** Company, employee, tool and model ids and outcomes may be tagged. Task text, member
  emails, prompts, inputs and repository paths may not.
- **Export is opt-in.** The Host turns on the OpenTelemetry SDK, with an OTLP exporter, only when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set. The standard `OTEL_*` variables configure it. Without that variable,
  nothing leaves the machine and no exporter runs.

## Consequences

Any OTLP backend (a collector, Grafana, Azure Monitor, Honeycomb and others) can chart jobs, tool outcomes and token
spend per company with no HireZero code for that backend. Telemetry is best-effort and unsigned: for disputes, the
audit log remains the record. The three OpenTelemetry packages are pinned in the Host lockfile like any other
dependency.
