# Project22

## Build and test

```text
dotnet build Project22.sln
dotnet test Project22.sln
```

## Design summary

`OrderIntakeService.Process(string json)` parses the input with `System.Text.Json`, checks the recognized JSON field types before validation, collects every applicable validation error, and returns either a rejected result or a strongly typed `LaboratoryOrder`. Accepted orders use `DateOnly`, canonical `Blood`/`Urine`/`Tissue`/`Saliva` specimen types, and canonical `Routine`/`Urgent` priorities. Unknown JSON properties are ignored.

The service trims string values before validation and storage. This is an explicit convenience assumption: whitespace around otherwise valid IDs, choices, dates, and test names is not preserved.

Malformed JSON, non-object roots, and incompatible recognized field types produce exactly one `$` / `MALFORMED_INPUT` error. A JSON object with missing or null recognized fields is treated as a shaped order and receives normal field-level errors. The implementation uses the machine's current local date for future-date validation.

## AI use

<!-- TODO: Describe your AI use here honestly. -->