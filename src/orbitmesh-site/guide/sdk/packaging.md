# Packaging and distribution

`dotnet publish` your project, then zip the publish output. That's the artifact the Server's Package Repository accepts - either uploaded by hand, or produced as a standard NuGet package (`content/` layout, `packageType="OrbitMeshApp"`) for distribution through a feed.

See [Package repository & distribution](/guide/architecture/packages) for how the Server tracks and updates feed-installed packages.
