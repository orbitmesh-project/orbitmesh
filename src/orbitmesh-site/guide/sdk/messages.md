# Messages (RPC between packages)

`[MessageHandler]` exposes a method other packages, or the Console, can call by key:

```csharp
[MessageHandler("GetSunInfo", Description = "Calculates sunrise/sunset for a date and location.")]
public SunInfo GetSunInfo(DateOnly date, int timezone, double latitude, double longitude) { /* ... */ }
```

Call `PackageHost.RegisterMessageHandlers(this)` once, typically in `OnStart`, to wire up every `[MessageHandler]` method on an instance.

## Key namespacing

The key is namespaced under the package's own name by default: `"DayInfo/GetSunInfo"` here, not `"GetSunInfo"`. Two unrelated packages picking the same key can't cross-trigger each other's handler. Callers use the qualified key:

```csharp
var result = await PackageHost.SendMessageAsync<SunInfo>(MessageScope.Create("DayInfo"), "DayInfo/GetSunInfo", new { date, timezone, latitude, longitude });
```

Pass `Shared = true` on `[MessageHandler]` (and `shared: true` on `SendMessage`/`SendMessageAsync`) for a raw, un-namespaced key - for a handler meant as a cross-package convention any caller can reach without knowing this package's name.

## Scope

`MessageScope` targets a single package, a group, the whole Edge, or everyone (`Package` / `Group` / `Edge` / `Others` / `All`). Pick the narrowest one that reaches who needs it.

Reaching another package - by name, group, or a broadcast scope - needs an `Allow` rule on the caller's own credential, which in turn needs the `messages:execute` permission scope (not to be confused with `MessageScope` above - unfortunate naming overlap). A package's own credential gets `messages:execute` automatically; a Console/Consumer credential calling `SendMessage` from outside a package needs it granted explicitly. See [Access control](/guide/architecture/access-control).

## Triggering a message on a schedule

[Scheduled Tasks](/guide/architecture/scheduled-tasks) fire a message on a cron schedule instead of a human clicking Invoke - same `Authorizations`/`messages:execute` check either way.
