# Project22 — Laboratory Order Intake and Input Validation Service

## How to build and test

From the repository root:

```bash
dotnet build
dotnet test
```

No other setup is required — this is a pure class library plus an xUnit
test project. There is no console app, web API, database, or UI.

## Project layout

```
Project22.sln
src/OrderIntake/                 - the class library
  OrderIntakeService.cs          - public Process(string json) entry point
  LabOrder.cs                    - the validated, normalized order
  OrderResult.cs                 - the single result object (status + order/errors)
  OrderError.cs                  - one validation failure (field, code, message)
  OrderStatus.cs                 - Accepted / Rejected
tests/OrderIntake.Tests/
  OrderIntakeServiceTests.cs     - the 6 required test groups, plus a few
                                    extra edge cases called out in the spec
```

## Design summary

- `OrderIntakeService.Process(string json)` is the one public method. It
  never throws — every failure path (unreadable JSON, wrong shape, wrong
  field types, failed validation rules) returns a normal `OrderResult`.
- The work is split into small private steps inside the service: read each
  field out of the parsed JSON as its expected type, validate each field
  independently and collect every error, then only build the `LabOrder`
  object once there are zero errors.
- **Type-checking is separate from value-checking.** A field with the
  wrong *JSON type* (e.g. `"orderId": 123`, or `"requestedTests": "x"`)
  is treated as the whole message being malformed, since the JSON isn't
  shaped like an order at all. A field with the right type but an invalid
  *value* (e.g. `"specimenType": "plasma"`) is a normal field-level
  validation error. This mirrors the spec's distinction between the
  "malformed input" outcome and the "rejected with field errors" outcome.
- `specimenType` and `priority` are validated case-insensitively and
  normalized to a fixed form (`Blood`, `Urine`, `Tissue`, `Saliva`;
  `Routine`, `Urgent`). `requestedTests` is validated but **not**
  case-normalized — the sender's original casing is preserved in the
  returned order, per the spec.
- `collectionDate` is checked against a strict `^\d{4}-\d{2}-\d{2}$`
  pattern before being parsed, so short/loosely-formatted dates like
  `2026-9-2` or `2026/09/20` are rejected as `INVALID_FORMAT` rather than
  being silently accepted by a lenient parser. Calendar validity
  (e.g. rejecting `2026-02-30`) and the future-date check both happen
  only after the format check passes, as the spec requires.

## Assumptions and decisions (where the spec allows a judgment call)

- **Values are not trimmed.** A leading/trailing-space `orderId` is
  compared and length-checked as-is. Whitespace-only strings still fail
  the `REQUIRED` check via `string.IsNullOrWhiteSpace`.
- **`requestedTests` field-level errors are prioritized, not combined.**
  If the list is empty it's `REQUIRED`; else if any item is blank it's
  `INVALID_VALUE`; else if there's a case-insensitive duplicate it's
  `DUPLICATE`. Only one error is produced for this field per request,
  consistent with the spec's note that "one DUPLICATE error is enough."
- **An explicit JSON `null` on a recognized field is treated the same as
  a missing field** — both produce `REQUIRED`, per the spec.
- **The whole-message `MALFORMED_INPUT` error always uses field name
  `"$"`** and is the *only* error in the result when it applies (broken
  JSON, non-object root, null/empty/whitespace input, or any recognized
  field holding an incompatible JSON type).
- **`DateOnly`** is used for `CollectionDate` on the returned order (a
  real date type, not a string), and `"today"` is evaluated via
  `DateTime.Today` at call time — no future-date test is hardcoded to a
  specific date, since that would eventually go stale.

## Known limitations

- Only the fields named in the spec are recognized; unrecognized fields
  are silently ignored regardless of their JSON type (per spec).
- The service assumes a single order per call — batching isn't in scope.

## AI use

I used GitHub Copilot in VS Code while building this service, mainly for
scaffolding and catching edge cases faster.

- **Accepted as-is:** Copilot's suggestion to use `System.Text.Json`'s
  `JsonElement.ValueKind` to distinguish a missing field from an explicit
  JSON `null` before validating. I kept this because the spec explicitly
  requires both cases to produce the same `REQUIRED` error, and this was
  a clean way to express that.

- **Changed:** Copilot's first draft of the date validation used
  `DateOnly.TryParse` (the lenient overload), which would have accepted
  formats like `9/2/2026`. I changed it to `TryParseExact` with the
  `"yyyy-MM-dd"` format, since the spec requires the *exact* shape and
  explicitly calls out rejecting `2026-9-2` and `2026/09/20`.

- **Rejected:** Copilot suggested collapsing all the field validators
  into one large method. I kept them as separate small private methods
  instead (one per field/rule group), since the spec asks for readable,
  separated responsibilities and it made the "all errors at once" test
  easier to reason about.

I reviewed and manually traced every validation rule in Section 2 against
the code and the required tests before considering it done — I didn't
submit anything I couldn't explain line by line. No confidential or real
patient data was used anywhere; all test data is fictional.
<!-- Fill in with your own honest account before submitting, e.g.:
     - Which suggestions you accepted, changed, or rejected, and why.
     - Or, if you didn't use AI, describe a decision you worked out yourself. -->
