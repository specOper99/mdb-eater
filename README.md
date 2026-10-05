# MDB Converter

Unpackaged WinUI 3 app. Opens any Access `.mdb` on Windows and writes local JSON plus a self-contained `postgres.sql`. The running app does not use the network.

Lives in this folder. The Flutter app is unchanged.

## Offline guarantee

A conversion finishes with the network adapter disabled.

- Scan, JSON/JSONL, `postgres.sql`, and Access object dumps use only the local `.mdb`, local ACE, local Microsoft Access (optional), and a folder you pick.
- The process does not open a socket. No live PostgreSQL connection, telemetry, update check, crash upload, remote fonts, WebView, or license call.
- NuGet restore happens at **build** time. The published app does not download anything when it runs.
- Linked tables are metadata only. Rows are not read, so ACE cannot follow a share or server.
- Settings live in `%LOCALAPPDATA%\MdbConverter\settings.json` (last paths, output folder, JSON/SQL toggles, Replace vs Skip). Database and workgroup passwords are not stored.

Apply `postgres.sql` later with `psql` or another client. That step is outside this app.

## What it exports

- Tables, columns, rows (memo and binary), primary keys, indexes, relationships
- Query SQL (not translated into PostgreSQL views; stored in JSON and in `access_queries`)
- System tables (`MSys*`) when the engine allows them
- Best-effort forms, reports, macros, and modules via Access COM `SaveAsText`

One failed object does not abort the run. Summary: written, skipped, failed.

## Prerequisites (Windows)

- Windows 10 1809+ or Windows 11
- .NET 8 SDK
- Visual Studio 2022 with the WinUI / Windows App SDK workload (to build the desktop app)
- Microsoft Access Database Engine 2016 **x64** (bitness must match the app)
- Full Microsoft Access, only if you need forms, reports, macros, and modules

No PostgreSQL server is required to run the converter.

This Mac can unit-test `MdbConverter.Core`. ACE, Access COM, and WinUI run on Windows only.

## Build

Core tests (any OS with the .NET 8 SDK):

```bash
dotnet test tests/MdbConverter.Core.Tests/MdbConverter.Core.Tests.csproj
```

Windows app (x64, self-contained Windows App SDK so the published build stays offline):

```bash
dotnet publish src/MdbConverter.App/MdbConverter.App.csproj -c Release -r win-x64 --self-contained true
```

Open `MdbConverter.sln` in Visual Studio on Windows to debug.

## Output layout

```
manifest.json
postgres.sql
data/<table>.jsonl
objects/forms/
objects/reports/
objects/macros/
objects/modules/
```

JSONL is one JSON object per line. Dates are ISO-8601. Binary/OLE is base64. Null stays null. PostgreSQL identifiers keep the Access name, quoted. Conflict mode **Replace** emits `DROP ... CASCADE`. **Skip** creates and loads only when the table is absent.
