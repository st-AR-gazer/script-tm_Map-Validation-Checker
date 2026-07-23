# Map Validation Checker

A CLI tool that inspects Trackmania `.Map.Gbx` files and reports how their Author Times appear to have been validated.

Don't want to run or build the executable? Use the [web version](https://tools.xjk.yt/Map-Validation-Checker/) instead.

## Usage

```powershell
MapValidationChecker --single "C:\Maps\MyMap.Map.Gbx"
MapValidationChecker --batch "C:\Maps" --recursive --pretty
MapValidationChecker --batch "C:\Maps" --replays "C:\Replays"
```

Useful options:

- `--manual <file>` — apply UID-based manual overrides
- `--no-gps` — disable GPS evidence
- `--strict-gps` — treat a GPS match as `Yes` instead of `Maybe`
- `--include-path` — include map and matching replay paths
- `--data-dump` — include parsed diagnostic details
- `--output <file>` — write the JSON result to a file
- `--help` — show every option

Manual override files may contain one object or an array:

```json
{ "uid": "SOME_MAP_UID", "valid": true, "note": "Reviewed manually" }
```

The CLI writes one JSON object in single mode and an array in batch mode.

## What it checks

```mermaid
flowchart TD
    Start["Extracted map facts"] --> Manual{"Manual override?"}

    Manual -- "valid=true" --> ManualYes["Yes / manual"]
    Manual -- "valid=false" --> ManualMaybe["Maybe / manual"]
    Manual -- "none" --> AT{"Author Time exists?"}

    AT -- "no" --> Missing["Unknown / normal<br/>missing AuthorMedal error"]
    AT -- "yes" --> Ghost{"Validation ghost exists?"}

    Ghost -- "matching time" --> GhostYes["Yes / validationghost"]
    Ghost -- "wrong time" --> GhostError["Unknown / validationghost<br/>mismatch error"]
    Ghost -- "none" --> Tag{"Removal tag exists?"}

    Tag -- "yes" --> TagTime{"Tag time matches current AT?"}
    TagTime -- "yes" --> TagYes["Yes / validationtag"]
    TagTime -- "no or missing" --> Replay{"Matching replay?"}
    Tag -- "no" --> Replay

    Replay -- "yes" --> ReplayYes["Yes / replay"]
    Replay -- "no" --> Waypoint{"Waypoint finish equals AT?"}

    Waypoint -- "yes, count plausible" --> NormalYes["Yes / normal"]
    Waypoint -- "yes, count implausible" --> PluginMaybe1["Maybe / plugin"]
    Waypoint -- "no or missing" --> GPS{"GPS enabled?"}

    GPS -- "yes" --> Scan["Scan GPS data"]
    Scan --> GpsMatch{"GPS match?"}
    GpsMatch -- "yes" --> StrictGps{"Strict GPS?"}
    StrictGps -- "yes" --> GpsYes["Yes / gps"]
    StrictGps -- "no" --> GpsMaybe["Maybe / gps"]
    GpsMatch -- "no" --> Fallback{"Usable waypoint metadata exists?"}

    GPS -- "no" --> Fallback
    Fallback -- "no" --> Unknown["Unknown / normal"]
    Fallback -- "yes, finish differs" --> PluginMaybe2["Maybe / plugin"]
```

Results use `Yes`, `Maybe`, or `Unknown`, together with a type such as `normal`, `plugin`, `gps`, or `replay`.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet build .\MapValidationChecker.sln -c Release
dotnet test .\MapValidationChecker.sln -c Release
dotnet run --project .\src\MapValidationChecker.Cli -- --help
```

## Disclaimer

This tool provides supporting evidence; it cannot _guarantee_ that an Author Time is legitimate.
