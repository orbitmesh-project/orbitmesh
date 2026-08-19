# Logging

```csharp
PackageHost.WriteInfo("Processed {0} items", count);
PackageHost.WriteWarn(...);
PackageHost.WriteError(...);
PackageHost.WriteDebug(...);
```

Shows up in the Console's Console log page, scoped to the Edge/package that wrote it.
