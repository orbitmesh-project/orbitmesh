# Access control

Every connection - Edge, package, Console, REST call - authenticates with an `AccessKey` tied to a named credential in the Server's config.

## Scopes

A credential holds a list of named **scopes** (PAT-style, like a GitHub token): `edges:read`, `packages:manage`, `credentials:manage`, `updates:manage`, and so on. See `OrbitMeshScope` for the full list. Each scope gates one slice of the Management API/`ControlHub`.

A credential with zero scopes still connects fine *as* an Edge or package - scopes only gate the admin-facing surfaces, not the base protocol.

## Authorization rules

`authorizations` is a finer-grained layer underneath scopes: per-target Allow/Deny rules for messages, groups and telemetry items. Same two-level model as AWS IAM - a scope authorizes the action class, a rule restricts the target.

Each package gets its own auto-provisioned Machine credential instead of sharing its Edge's. It defaults to `Deny` on all three, with just enough `Allow` rules to reach its own edge/package's telemetry, its own configured groups, and messages addressed to itself:

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

Reaching another package's telemetry, or sending it a targeted message, needs an explicit `Allow` rule on that credential (Console → Credentials → Authorizations). Broadcasting to everyone (`All`/`Others` message scope) needs one too.
