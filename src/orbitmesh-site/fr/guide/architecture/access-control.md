# Contrôle d'accès

Chaque connexion - Edge, package, Console, appel REST - s'authentifie avec un `AccessKey` lié à un identifiant nommé dans la config du Server.

## Scopes

Un identifiant porte une liste de **scopes** nommés (façon PAT, comme un token GitHub) : `edges:read`, `packages:manage`, `credentials:manage`, `updates:manage`, etc. Voir `OrbitMeshScope` pour la liste complète. Chaque scope couvre une tranche de la Management API/du `ControlHub` - ou, pour trois scopes ci-dessous, le protocole de base Edge/package/consumer lui-même.

L'identifiant propre à un Edge a besoin de `edge:connect` juste pour ouvrir sa connexion à `EdgeHub` ; l'identifiant propre à un package a besoin de `package:connect` pour ouvrir sa connexion à `OrbitMeshHub`, et de `messages:execute` pour envoyer un message via `SendMessage` (un identifiant Console/Consumer a besoin du même scope pour invoquer un message handler via `ConsumerHub`/l'endpoint REST `SendMessage`). Sans le scope correspondant, `Authorizations` ci-dessous n'est même pas consulté - un identifiant sans `edge:connect`/`package:connect` ne peut pas se connecter du tout, peu importe ce que dit `Authorizations`. L'identifiant d'un package reçoit les deux scopes automatiquement (`EnsurePackageCredential`, réappliqué à chaque déploiement ou changement de groupes) ; un Edge approuvé depuis la liste d'attente reçoit `edge:connect` de la même façon. Tous les autres scopes restent purement liés à l'administration.

## Règles d'autorisation

`authorizations` est une couche plus fine sous les scopes : des règles Allow/Deny par cible pour les messages, groupes et telemetry items. Même modèle à deux niveaux qu'AWS IAM - un scope autorise la classe d'action, une règle restreint la cible.

Chaque package reçoit son propre identifiant Machine auto-provisionné, au lieu de partager celui de son Edge. Par défaut `Deny` sur les trois, avec juste assez de règles `Allow` pour atteindre sa propre télémétrie edge+package, ses propres groupes configurés, et les messages adressés à lui-même :

```json
{
  "name": "salon.DayInfo",
  "accessKey": "ENC$...",
  "kind": "Machine",
  "scopes": ["package:connect", "messages:execute"],
  "authorizations": {
    "messages": {
      "defaultAuthorization": "Deny",
      "rules": [
        { "authorization": "Allow", "scope": "Package", "args": "DayInfo" },
        { "authorization": "Allow", "scope": "Edge", "args": "salon" }
      ]
    },
    "groups": { "defaultAuthorization": "Deny", "rules": [] },
    "telemetryItems": {
      "defaultAuthorization": "Deny",
      "rules": [{ "authorization": "Allow", "edgeName": "salon", "packageName": "DayInfo", "name": "*" }]
    }
  }
}
```

Atteindre la télémétrie d'un autre package, ou lui envoyer un message ciblé, nécessite une règle `Allow` explicite sur cet identifiant (Console → Credentials → Authorizations). La diffusion à tout le monde (portée de message `All`/`Others`) aussi.
