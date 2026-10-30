using System.Text.Json.Nodes;

namespace OrbitMesh.Server.Services;

/// <summary>Masks the secrets in the raw configuration the Management API hands out, and puts the real
/// values back when the (possibly edited) document is posted back. The Console's raw JSON editor
/// round-trips the whole "OrbitMesh" section, so masking has to be reversible or saving would overwrite
/// every secret with its placeholder.
/// <para>Covered: credential AccessKeys (PBKDF2 hashes / encrypted blobs - offline-crackable or
/// reusable by whoever holds them), the update server token, the single-sign-on client secret, NuGet feed API keys, secret variables, and
/// package settings whose manifest type is Password.</para></summary>
public static class ConfigurationRedactor
{
    public const string Placeholder = "********";

    /// <summary>Tells whether a package setting is a Password-typed one (per the package's manifest).</summary>
    public delegate bool IsPasswordSetting(string edgeName, string packageName, string settingName);

    public static void Redact(JsonNode section, IsPasswordSetting isPassword)
    {
        Visit(section, current: null, isPassword, restore: false, unresolved: null);
    }

    /// <summary>Replaces every placeholder in <paramref name="incoming"/> with the matching current value.
    /// Returns null on success, or a message naming a placeholder that has nothing to restore (a new item
    /// saved with the placeholder instead of a real secret).</summary>
    public static string? Restore(JsonNode incoming, JsonNode current, IsPasswordSetting isPassword)
    {
        var unresolved = new List<string>();
        Visit(incoming, current, isPassword, restore: true, unresolved);
        return unresolved.Count == 0
            ? null
            : $"'{Placeholder}' is only a placeholder for an existing secret, but there is no current value to restore for: {string.Join(", ", unresolved)}. Enter the real value.";
    }

    private static void Visit(JsonNode section, JsonNode? current, IsPasswordSetting isPassword, bool restore, List<string>? unresolved)
    {
        if (section is not JsonObject root)
        {
            return;
        }
        var currentRoot = current as JsonObject;

        Apply(root, currentRoot, "updateOptions", "token", restore, unresolved, "updateOptions.token");
        if (root["auth"] is JsonObject auth)
        {
            Apply(auth, (currentRoot?["auth"] as JsonObject), "oidc", "clientSecret", restore, unresolved, "auth.oidc.clientSecret");
        }

        foreach (var (item, currentItem) in Pairs(root, currentRoot, "credentials", "name"))
        {
            Apply(item, currentItem, "accessKey", restore, unresolved, $"credential '{Str(item, "name")}'");
        }
        foreach (var (item, currentItem) in Pairs(root, currentRoot, "nuGetFeeds", "name"))
        {
            Apply(item, currentItem, "apiKey", restore, unresolved, $"NuGet feed '{Str(item, "name")}'");
        }
        foreach (var (item, currentItem) in Pairs(root, currentRoot, "variables", "name"))
        {
            if (item["isSecret"] is JsonValue flag && flag.TryGetValue<bool>(out var secret) && secret)
            {
                Apply(item, currentItem, "value", restore, unresolved, $"variable '{Str(item, "name")}'");
            }
        }
        foreach (var (edge, currentEdge) in Pairs(root, currentRoot, "edges", "name"))
        {
            foreach (var (package, currentPackage) in Pairs(edge, currentEdge, "packages", "name"))
            {
                if (package["settings"] is not JsonObject settings)
                {
                    continue;
                }
                var currentSettings = currentPackage?["settings"] as JsonObject;
                foreach (var key in settings.Select(p => p.Key).ToList())
                {
                    if (isPassword(Str(edge, "name"), Str(package, "name"), key))
                    {
                        Apply(settings, currentSettings, key, restore, unresolved, $"setting '{key}' of {Str(edge, "name")}/{Str(package, "name")}");
                    }
                }
            }
        }
    }

    private static void Apply(JsonObject parent, JsonObject? currentParent, string section, string property, bool restore, List<string>? unresolved, string label)
    {
        if (parent[section] is JsonObject child)
        {
            Apply(child, currentParent?[section] as JsonObject, property, restore, unresolved, label);
        }
    }

    private static void Apply(JsonObject item, JsonObject? currentItem, string property, bool restore, List<string>? unresolved, string label)
    {
        if (item[property] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return;
        }
        if (!restore)
        {
            if (!string.IsNullOrEmpty(text))
            {
                item[property] = Placeholder;
            }
            return;
        }
        if (text != Placeholder)
        {
            return;
        }
        if (currentItem?[property] is JsonValue currentValue && currentValue.TryGetValue<string>(out var currentText))
        {
            item[property] = currentText;
        }
        else
        {
            unresolved!.Add(label);
        }
    }

    // Yields each object in parent[arrayName] together with the same-named object in the current document
    // (null if it's new, or when only redacting).
    private static IEnumerable<(JsonObject Item, JsonObject? Current)> Pairs(JsonObject? parent, JsonObject? currentParent, string arrayName, string keyProperty)
    {
        if (parent?[arrayName] is not JsonArray array)
        {
            yield break;
        }
        var currentArray = currentParent?[arrayName] as JsonArray;
        foreach (var node in array)
        {
            if (node is not JsonObject item)
            {
                continue;
            }
            var key = Str(item, keyProperty);
            var match = currentArray?
                .OfType<JsonObject>()
                .FirstOrDefault(c => string.Equals(Str(c, keyProperty), key, StringComparison.OrdinalIgnoreCase));
            yield return (item, match);
        }
    }

    private static string Str(JsonObject obj, string property) =>
        obj[property] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;
}
