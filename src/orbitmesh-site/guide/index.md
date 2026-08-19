# What is OrbitMesh?

OrbitMesh is a self-hosted platform for running small automation packages - weather feeds, network tools, smart-home integrations, and anything else you write yourself - on your own devices, managed from one place.

It's stateless. No database records history. The Console always shows the current state a package last reported, never a log of past values.

## Components

- **Server** - the central hub. Tracks connected Edges, holds the package repository, pushes configuration and updates.
- **Edge** - runs on each device (a Raspberry Pi, a home server, a kiosk screen...). Downloads, runs and supervises its assigned packages. Each package reports its own telemetry straight to the Server, not through the Edge.
- **Console** - the web UI: edges, packages, credentials, telemetry, logs.
- **Packages** - the automations themselves. Small .NET apps built against the OrbitMesh Package SDK (see [Building a package](/guide/sdk/)).

## Installing a package

A package reaches an Edge two ways:

1. **Manual upload** - drop a `.zip` built by the package's own project into the Server's Package Repository from the Console.
2. **From a NuGet feed** - the Server browses and installs directly from any standard NuGet V3 feed (see [Packages](/packages) for the official ones), including update checks.

Both paths end at the same place: the Server's package repository, ready to assign to an Edge.
