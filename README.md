# Playbook

A professional football intelligence platform focused on maximizing fantasy football decision making through explainable recommendations.

This is **not** a fantasy football website. It is a personal football intelligence platform that prioritizes concise, actionable recommendations over raw data — while remaining fully explainable.

## Vision

When you open Playbook, you should immediately understand the highest-impact decisions you can make. The system will continuously ingest football information, convert it into recommendations, explain every recommendation clearly, and adapt as news breaks or rosters change.

See [docs/DESIGN.md](docs/DESIGN.md) for the full product design.

## Solution Structure

```
playbook.sln
├── src
│   ├── Playbook.Web            # Blazor Server UI
│   ├── Playbook.Core           # Domain models & abstractions
│   ├── Playbook.Application    # Use cases & application services
│   └── Playbook.Infrastructure # Persistence & external adapters (stubbed)
├── tests
│   └── Playbook.Tests          # xUnit tests
└── docs
    ├── DESIGN.md
    ├── ARCHITECTURE.md
    └── DEVELOPMENT_RULES.md
```

## Tech Stack

| Area | Choice |
| --- | --- |
| Backend | ASP.NET Core (.NET 9) |
| UI | Blazor Server (Interactive Server render mode) |
| Data access | Entity Framework Core + PostgreSQL *(planned; not wired yet)* |
| Testing | xUnit |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Setup

```bash
git clone <repository-url>
cd playbook
dotnet restore
dotnet build
```

## Run the web app

```bash
dotnet run --project src/Playbook.Web
```

Then open the URL shown in the console (typically `https://localhost:7xxx`).

## Run tests

```bash
dotnet test
```

## Current Status

**Application shell complete under the Playbook brand.** The solution builds with layered projects, DI composition, and a Blazor shell (sidebar, top bar, dashboard + placeholder feature pages). No database, APIs, or fantasy logic have been implemented yet.

## Documentation

- [Design](docs/DESIGN.md) — product vision, pages, and engines
- [Architecture](docs/ARCHITECTURE.md) — layered structure and future engine pipeline
- [Development Rules](docs/DEVELOPMENT_RULES.md) — engineering standards for this repo
- [Changelog](docs/CHANGELOG.md) — notable project changes
