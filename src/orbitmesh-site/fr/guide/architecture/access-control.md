# Contrôle d'accès

Chaque connexion - Edge, package, Console, appel REST - s'authentifie avec un `AccessKey` lié à un identifiant nommé dans la config du Server.

## Scopes

Un identifiant porte une liste de **scopes** nommés (façon PAT, comme un token GitHub) : `edges:read`, `packages:manage`, `credentials:manage`, `updates:manage`, etc. Voir `OrbitMeshScope` pour la liste complète. Chaque scope couvre une tranche de la Management API/du `ControlHub`.

Un identifiant sans aucun scope se connecte quand même normalement *en tant qu*'Edge ou package - les scopes ne couvrent que les surfaces d'administration, pas le protocole de base.

## Règles d'autorisation

`authorizations` est une couche plus fine sous les scopes : des règles Allow/Deny par cible pour les messages, groupes et telemetry items. Même modèle à deux niveaux qu'AWS IAM - un scope autorise la classe d'action, une règle restreint la cible.

Chaque package reçoit son propre identifiant Machine auto-provisionné, au lieu de partager celui de son Edge. Par défaut `Deny` sur les trois, avec juste assez de règles `Allow` pour atteindre sa propre télémétrie edge+package, ses propres groupes configurés, et les messages adressés à lui-même :

```json
{
  "name": "salon.DayInfo",
  "accessKey": "ENC$...",
  "kind": "Machine",
  "scopes": [],
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
