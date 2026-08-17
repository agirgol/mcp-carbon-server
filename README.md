# mcp-carbon-server

[![NuGet](https://img.shields.io/nuget/vpre/McpCarbonServer.svg)](https://www.nuget.org/packages/McpCarbonServer)
[![ci](https://github.com/agirgol/mcp-carbon-server/actions/workflows/ci.yml/badge.svg)](https://github.com/agirgol/mcp-carbon-server/actions/workflows/ci.yml)

An [MCP](https://modelcontextprotocol.io/) server that gives an LLM client real greenhouse
gas accounting: Scope 1/2/3 calculation over a versioned, source-cited emission factor
catalog, with unit conversion and AR5/AR6 GWP set selection.

Calculation is not done here. It is done by
[GhgAccounting](https://github.com/agirgol/carbon-accounting-dotnet), a standalone library
aligned to the GHG Protocol and ISO 14064-1. This repository is the protocol surface over
it: tool definitions, schemas, transport and packaging. The split is deliberate — the
accounting logic has to be usable from an ERP or a batch job, not only from a chat client.

## Why it exists

An LLM asked to work out a company's footprint will produce a number. Without tools it
produces that number from memory: an emission factor it half-remembers, no version, no
geography, no citation. The figure looks like every correct figure and cannot be audited.

This server replaces that with a lookup against a compiled catalog. Every result carries
the factor it used, the dataset that published it, the publication year, and whether
those numbers have been verified against the cited source.

![Searching the factor catalog, applying a factor, building an inventory, and having a
mismatched unit refused](demo/demo.gif)

The recording is generated from [`demo/demo.tape`](demo/demo.tape) against the published
tool — `vhs demo/demo.tape` re-renders it. The tape is the source and the GIF is a build
artifact, so when the tool surface changes the demo is regenerated rather than re-recorded,
and it cannot quietly drift into showing a command that no longer exists.

`demo/carbon` is the small helper the recording drives: it performs a handshake, makes one
tool call and prints the structured result, so the tools can be shown without a chat client
in the frame. It is a demo aid rather than a client — a real MCP host keeps one server
process for the whole session.

## Tools

| Tool | What it does |
|---|---|
| `list_factor_sets` | Datasets compiled into this build, with publisher, coverage and verification status |
| `search_emission_factors` | Find factor ids by activity wording, scope, region or dataset; reports how many matched as well as how many it returned |
| `calculate_emissions` | Apply one factor to one activity figure; returns CO2e with per-gas breakdown and provenance |
| `build_inventory` | Aggregate many lines into scope 1/2/3 totals, scope 2 both ways, scope 3 by category |
| `convert_units` | Convert between units of the same physical dimension |

## Resources

Attachable context, projected from the compiled catalog rather than written out, so a
resource cannot drift from what the tools compute.

| URI | What it is |
|---|---|
| `carbon://factor-sets` | Every dataset in this build, with publisher, coverage, licence and verification status |
| `carbon://factor-sets/{setId}` | One dataset in full, including every factor it publishes |
| `carbon://gwp/{gwpSet}` | The global warming potentials actually compiled in for one assessment report |

The last one is worth attaching when a disclosure has to state which potentials it used:
the answer is a property of the numbers shipped, not of what the report says in general.

## Prompts

| Prompt | What it frames |
|---|---|
| `ghg_inventory_intake` | Collecting activity data and turning it into a scope 1/2/3 inventory |
| `disclosure_review` | Checking draft figures against what a disclosure has to carry, reporting gaps rather than filling them |

## Install

Published as a .NET global tool. Releases are pre-release for now — the tool surface is
still settling and only the stdio transport is implemented — so the flag is required:

```sh
dotnet tool install -g McpCarbonServer --prerelease
```

Then point an MCP host at the `mcp-carbon-server` command. For Claude Desktop, in
`claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "carbon": {
      "command": "mcp-carbon-server"
    }
  }
}
```

## Design notes

**stdout belongs to the protocol.** Under the stdio transport, stdout carries JSON-RPC
frames and nothing else. Every log line goes to stderr, and the generic host's default
console logger is removed at startup rather than reconfigured — a single stray write
desynchronises the framing and the host drops the server without surfacing an error.

**Units are carried, not assumed.** Activity data is accepted in any unit measuring the
same physical quantity as the factor's denominator and converted. A unit from another
dimension is rejected instead of being coerced through an assumed density or calorific
value.

**Scope 2 is reported twice.** Location-based and market-based are separate figures under
the GHG Protocol, and only one of them belongs in a given total. `build_inventory` returns
both and asks which the headline total should use; requesting a method no line reports is
an error rather than a total that quietly omits scope 2.

**Biogenic carbon sits outside the total.** It is disclosed separately, as the standard
requires, instead of being folded into the scopes.

**Verification status travels with the number.** Factor sets carry a status, and it is
returned on every result. A figure derived from an unverified set is not a disclosure and
the response says so.

**Results are data, not prose.** Every tool publishes an output schema and returns
structured content, so a client gets a validated object rather than a string to parse. The
tools are annotated read-only and idempotent — they are pure functions over a catalog
compiled into the binary, reading nothing outside the process — so a client can call them
without an approval prompt.

## Building from source

The server depends on `GhgAccounting`. When a checkout of
[carbon-accounting-dotnet](https://github.com/agirgol/carbon-accounting-dotnet) sits
beside this repository, the build references that project directly, so library changes
show up on the next build with no pack/restore cycle. Otherwise it falls back to the
published package. Neither case needs a flag set.

```sh
git clone https://github.com/agirgol/mcp-carbon-server
git clone https://github.com/agirgol/carbon-accounting-dotnet   # optional
dotnet build McpCarbonServer.slnx
```

To build against the published package while the sibling checkout is present — worth doing
before tagging a release, since the two do not compile in the same factor catalog:

```sh
dotnet build McpCarbonServer.slnx -p:UseLocalGhgAccounting=false
```

## Licence

MIT. Emission factor data carries the licence of its publisher; see the `NOTICE` file in
[carbon-accounting-dotnet](https://github.com/agirgol/carbon-accounting-dotnet).
