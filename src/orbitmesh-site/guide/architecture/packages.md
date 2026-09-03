# Package repository & distribution

![Packages page listing installed packages per edge, with status/version/resource usage](/screenshots/packages.jpg)

The Server's package repository accepts a package two ways:

- **Manual `.zip` upload.**
- **Install from a feed** - any standard NuGet V3 source.

See [Packaging and distribution](/guide/sdk/packaging) for how a package is packed for either path.

Feed-installed packages get a small provenance sidecar recorded next to them. The Server uses it to check that feed for updates later.

`install.sh`/`install.ps1` preconfigure the official OrbitMesh feed (`https://nuget.orbitmesh.org/feeds/OrbitMesh/v3/index.json`) in `nuGetFeeds`. It's a list - edit or add entries there for a private/self-hosted feed (e.g. a [Pépite](https://github.com/orbitmesh-project/orbitmesh) instance) alongside or instead of it.
