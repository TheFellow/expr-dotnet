# Wedge breadcrumbs

A `wedged` ledger event must point to `semport/wedged/<full-sha>.md`. Wedges are
an explicit escape hatch after implementation cannot pass the quality gates.
The breadcrumb records:

- the full SHA and upstream metadata;
- the attempted semantic port and recoverable Git stash name;
- exact validation or review failures;
- files and tests involved; and
- a concrete recovery plan and tracking issue when one exists.

Wedged does not mean compatible. It keeps later upstream review moving while
making parity debt visible and mechanically auditable.
