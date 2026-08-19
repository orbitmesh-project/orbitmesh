# Dépôt de packages et distribution

Le dépôt de packages du Server accepte un package de deux façons :

- **Upload manuel de `.zip`.**
- **Installation depuis un feed** - n'importe quelle source NuGet V3 standard.

Voir [Packaging et distribution](/fr/guide/sdk/packaging) pour comment un package est packagé pour l'un ou l'autre chemin.

Les packages installés depuis un feed reçoivent un petit sidecar de provenance enregistré à côté d'eux. Le Server s'en sert pour vérifier ce feed plus tard.

`install.sh`/`install.ps1` préconfigurent le feed officiel OrbitMesh (`https://nuget.orbitmesh.org/feeds/OrbitMesh/v3/index.json`) dans `nuGetFeeds`. C'est une liste - modifiez ou ajoutez-y des entrées pour un feed privé/auto-hébergé (ex. une instance Pépite) en plus ou à la place.
