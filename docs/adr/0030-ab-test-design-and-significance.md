# 0030: A/B test design and significance

Status: Accepted (Phase 4).

## Context

Phase 4 asks Marketing for A/B test design with significance calculation. Language models are unreliable at
arithmetic and at statistics. A wrong sample size wastes weeks of traffic, and a wrong p-value ships a change that
did nothing.

## Decision

- **Code computes; the model writes.** Two deterministic actions do all the arithmetic, and their output is an
  artifact for the owner's review ([ADR 0011](0011-employee-actions.md)). Neither calls a tool or a model.
  - `ab-design` takes:
    - the current conversion rate;
    - the smallest relative lift worth detecting;
    - the visitors per day;
    - the variants (2 to 5, including control);
    - the significance level and the power.

    It returns the visitors each variant needs for a two-sided two-proportion z-test, the total, and the run length
    rounded up to whole weeks so every weekday counts equally. It warns beyond eight weeks. With more than one
    variant against the control, the significance level is shared among the comparisons (Bonferroni).
  - `ab-significance` takes a control's and a variant's visitors and conversions and the significance level. It
    returns:
    - both rates and the difference, with its confidence interval (Wald, unpooled);
    - the relative lift;
    - z and the two-sided p-value (pooled);
    - whether the result is significant;
    - a sample-ratio check: a chi-square test of the split against 50/50. A mismatch (p < 0.001) is reported first,
      as a warning not to trust the result until its cause is found.
- **The skill only words it.** The measured `ab-test` skill writes either a plan (hypothesis, one primary metric,
  variants, audience, guardrails, sample and length from the evidence, no early stopping) or a readout (significance,
  lift and interval, recommendation). It may state only numbers that appear in the approved evidence.
- **Fixed-horizon tests only.** The plan says to run the full length, and the readout says the result is valid only
  then. Sequential testing and Bayesian readouts can come later as separate actions.
- **Checked against an outside reference.** The normal distribution (Cody's erfc, Acklam's quantile), the sample sizes
  and the test results are checked against values computed independently with Python's `statistics.NormalDist`.

## Consequences

The owner can size a test before building it, and read its result without trusting a model's arithmetic. Results for
non-conversion metrics (revenue per visitor, time on page) need other tests and are not covered yet.
