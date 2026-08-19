# Qu'est-ce qu'OrbitMesh ?

OrbitMesh est une plateforme auto-hébergée pour faire tourner de petits packages d'automatisation - météo, outils réseau, intégrations domotiques, ou tout ce que vous écrivez vous-même - sur vos propres appareils, gérés depuis un seul endroit.

Elle est stateless. Aucune base de données n'enregistre d'historique. La Console affiche toujours le dernier état rapporté par un package, jamais un journal des valeurs passées.

## Composants

- **Server** - le hub central. Suit les Edges connectés, héberge le dépôt de packages, pousse la configuration et les mises à jour.
- **Edge** - tourne sur chaque appareil (un Raspberry Pi, un serveur maison, un écran kiosque...). Télécharge, exécute et supervise ses packages assignés. Chaque package remonte sa propre télémétrie directement au Server, pas via l'Edge.
- **Console** - l'interface web : edges, packages, identifiants, télémétrie, logs.
- **Packages** - les automatisations elles-mêmes. De petites apps .NET construites avec le SDK OrbitMesh Package (voir [Créer un package](/fr/guide/sdk/)).

## Installer un package

Un package atteint un Edge de deux façons :

1. **Upload manuel** - déposez un `.zip` produit par le projet du package dans le dépôt de packages du Server, depuis la Console.
2. **Depuis un feed NuGet** - le Server parcourt et installe directement depuis n'importe quel feed NuGet V3 standard (voir [Packages](/fr/packages) pour les officiels), y compris les mises à jour.

Les deux chemins finissent au même endroit : le dépôt de packages du Server, prêt à être assigné à un Edge.
