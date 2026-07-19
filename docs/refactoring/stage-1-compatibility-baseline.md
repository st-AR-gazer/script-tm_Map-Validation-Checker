# Stage 1 compatibility baseline

This document records the observable behavior that the staged refactor should preserve until a change is explicitly approved. The baseline was captured from commit `81e436c` with .NET SDK `10.0.103`.

## Compatibility boundary

The compatibility contract covers:

- validation precedence and final classification;
- JSON field names, values, and null emission;
- single-object versus batch-array output shape;
- command-line validation, exit codes, and stdout/stderr routing;
- `--pretty`, `--include-path`, `--no-map-name`, and `--output` behavior.

JSON whitespace and object-property ordering are not contracts. Path ordering in a batch scan is also not guaranteed.

## Exit and stream behavior

| Situation | Exit code | stdout | stderr |
| --- | ---: | --- | --- |
| Successful scan, including a per-file error report | 0 | JSON | Empty unless progress is enabled |
| Invalid command-line arguments | 2 | Help text | Argument error |
| `--help` | 2 | Help text | `No arguments provided.` |
| Unhandled fatal error | 1 | Unspecified | Full fatal exception |

`--output` writes the same JSON payload to the requested file and still prints it to stdout. The file has no appended newline; `Console.WriteLine` appends a newline to stdout.

## Report shape

Every serialized report currently emits all of these camel-case properties, including properties whose values are null:

```text
uid
validated
type
note
gpsValidation
path
mapName
replayPath
error
dataDump
```

Single mode emits one report object. Batch mode emits an array. A file without the `GBX` magic prefix produces `error: "not a gbx file"`; malformed data with the prefix produces `error: "failed to parse map gbx"`. Both are report-level outcomes with exit code 0.

## Validation precedence

The first terminal result wins in this order:

1. Manual override
2. Validation ghost, including a terminal mismatch error
3. Validation-removal metadata tag
4. Matching replay evidence
5. Matching waypoint metadata with plausible checkpoint count
6. GPS evidence when enabled
7. Missing-metadata or plugin-suspicion fallback

The precedence itself is a compatibility requirement. It will be made explicit and unit-tested when Stage 3 introduces typed validation inputs; it must not be reordered during mechanical extraction.

## GPS baseline

GPS candidate preference is currently:

1. exact media-block chunk `U05` match;
2. `RecordData.EntList[*].U03` within the configured threshold;
3. `U03 - 3000` within the configured threshold.

An exact `U05` match still reports `validated: "Maybe"` unless `--strict-gps` is supplied. The two ignored local maps present during baseline capture both exercised this exact-U05 path. They are not committed fixtures and the test suite does not assume they exist.

## Deliberately deferred behavior changes

The following are documented quirks, not Stage 1 fixes:

- `--help` is treated as an argument error.
- replay parse failures are silently ignored;
- null report fields are serialized;
- manual JSON recovery performs raw `True`/`False` replacement;
- the generated executable is tracked in Git;
- the README SDK guidance does not match the `net10.0` target.

## Automated coverage and fixture policy

The characterization project launches the built CLI as a child process. It covers argument failures, help output, success/error exit behavior, compact and pretty JSON, report shape, single and batch modes, path inclusion, malformed input, output-file mirroring, and the missing-author result from a valid empty GBX generated at test runtime.

No real `.Map.Gbx` file is added to source control in Stage 1. The repository ignores those files, and the local maps may contain user-created content. Pure validation branches will receive exhaustive tests against typed inputs in Stage 3. A small sanitized GBX integration fixture can be added later with explicit approval.
