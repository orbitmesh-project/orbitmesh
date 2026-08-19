# Packaging et distribution

`dotnet publish` votre projet, puis zippez la sortie du publish. C'est l'artefact que le Package Repository du Server accepte - soit uploadé à la main, soit produit comme un package NuGet standard (structure `content/`, `packageType="OrbitMeshApp"`) pour distribution via un feed.

Voir [Dépôt de packages et distribution](/fr/guide/architecture/packages) pour comment le Server suit et met à jour les packages installés depuis un feed.
