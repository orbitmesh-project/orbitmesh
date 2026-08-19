using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace OrbitMesh.Utils;

/// <summary>Enriches PackageDescriptor entries with descriptions pulled from the package's XML doc comments file, if present.</summary>
internal static partial class XmlDocumentationReader
{
    // PackageDescriptor generation calls into this once per type/property/method/enum value - caching
    // the parsed document per assembly avoids reloading and re-parsing the same .XML file from disk
    // dozens of times for a package with many callbacks/types.
    private static readonly ConcurrentDictionary<string, XmlDocument?> DocumentCache = new();

    [GeneratedRegex("<[^>]+\\scref\\b=\"([^\"\\s*/>]*)\"\\s*/>")]
    private static partial Regex CrefRegex();

    internal static void AddEnumXmlComment(string enumValue, Type enumType, PackageDescriptor.MemberDescriptor descriptor)
    {
        var node = GetXmlCommentNode(enumType, $"F:{enumType.FullName!.Replace("+", ".")}.{enumValue}");
        if (node != null && string.IsNullOrEmpty(descriptor.Description))
        {
            descriptor.Description = GetDescriptionText(node.SelectSingleNode("summary")?.InnerXml ?? string.Empty);
        }
    }

    internal static void AddXmlComment(Type type, PackageDescriptor.TypeDescriptor descriptor)
    {
        var node = GetXmlCommentNode(type, $"T:{type.FullName!.Replace("+", ".")}");
        if (node != null && string.IsNullOrEmpty(descriptor.Description))
        {
            descriptor.Description = GetDescriptionText(node.SelectSingleNode("summary")?.InnerXml ?? string.Empty);
        }
    }

    internal static void AddXmlComment(PropertyInfo property, PackageDescriptor.MemberDescriptor descriptor)
    {
        var node = GetXmlCommentNode(property.DeclaringType!, $"P:{property.DeclaringType!.FullName!.Replace("+", ".")}.{property.Name}");
        if (node != null && string.IsNullOrEmpty(descriptor.Description))
        {
            descriptor.Description = GetDescriptionText(node.SelectSingleNode("summary")?.InnerXml ?? string.Empty);
        }
    }

    internal static void AddXmlComment(MethodInfo method, PackageDescriptor.MessageHandlerDescriptor descriptor)
    {
        var node = GetXmlCommentNode(method.DeclaringType!, $"M:{method.DeclaringType!.FullName!.Replace("+", ".")}.{method.Name}");
        if (node == null)
        {
            return;
        }
        if (string.IsNullOrEmpty(descriptor.Description))
        {
            descriptor.Description = GetDescriptionText(node.SelectSingleNode("summary")?.InnerXml ?? string.Empty);
        }
        foreach (var parameter in descriptor.Parameters)
        {
            var paramNode = node.SelectSingleNode($"//param[@name='{parameter.Name}']");
            if (paramNode?.InnerText != null)
            {
                parameter.Description = GetDescriptionText(paramNode.InnerText);
            }
        }
    }

    private static XmlNode? GetXmlCommentNode(Type typeSource, string path)
    {
        var location = typeSource.Assembly.Location;
        if (string.IsNullOrEmpty(location))
        {
            return null;
        }
        var xmlDocPath = location[..location.LastIndexOf('.')] + ".XML";
        var doc = DocumentCache.GetOrAdd(xmlDocPath, LoadDocument);
        return doc?.SelectSingleNode($"//member[starts-with(@name, '{path}')]");
    }

    private static XmlDocument? LoadDocument(string xmlDocPath)
    {
        try
        {
            if (!File.Exists(xmlDocPath))
            {
                return null;
            }
            var doc = new XmlDocument();
            doc.Load(xmlDocPath);
            return doc;
        }
        catch
        {
            return null;
        }
    }

    private static string GetDescriptionText(string xml) => StripTags(CrefRegex().Replace(xml, "$1"));

    private static string StripTags(string source)
    {
        var buffer = new char[source.Length];
        var length = 0;
        var insideTag = false;
        foreach (var c in source)
        {
            switch (c)
            {
                case '<':
                    insideTag = true;
                    continue;
                case '>':
                    insideTag = false;
                    continue;
            }
            if (!insideTag)
            {
                buffer[length++] = c;
            }
        }
        return new string(buffer, 0, length).Trim();
    }
}
