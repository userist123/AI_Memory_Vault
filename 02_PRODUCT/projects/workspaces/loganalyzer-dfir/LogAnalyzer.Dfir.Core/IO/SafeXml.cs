using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.Linq;

namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// The one way to parse XML that comes from evidence or from an imported file (GPO backups and preferences, event XML; security audit #12).
/// DTD processing is prohibited (a <c>&lt;!DOCTYPE&gt;</c> throws <see cref="XmlException"/>), no resolver is set (no external entity, schema or
/// DTD is ever fetched), entity expansion is capped and the document size is bounded. Predefined (<c>&amp;amp;</c>) and character (<c>&amp;#x41;</c>)
/// references still work.
/// </summary>
/// <remarks>
/// <see cref="XmlReaderSettings.MaxCharactersFromEntities"/> is set to a small positive number, not 0: in .NET 0 means "no limit". With DTDs prohibited
/// no entity can be declared anyway; the cap is a second line of defence.
/// </remarks>
public static class SafeXml
{
    /// <summary>Default upper bound on the characters of one document (64 Mi characters).</summary>
    public const long DefaultMaxCharacters = 64L * 1024 * 1024;

    public static XmlReaderSettings Settings(long maxCharacters = DefaultMaxCharacters) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 1024,
        MaxCharactersInDocument = maxCharacters,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    /// <summary>Parses <paramref name="xml"/>; throws <see cref="XmlException"/> for a DOCTYPE, an entity, malformed XML or an oversized document.</summary>
    public static XDocument Parse(string xml, LoadOptions options = LoadOptions.None, long maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using var reader = XmlReader.Create(new StringReader(xml), Settings(maxCharacters));
        return XDocument.Load(reader, options);
    }

    /// <summary>As <see cref="Parse(string, LoadOptions, long)"/>, from a file.</summary>
    public static XDocument Load(string path, LoadOptions options = LoadOptions.None, long maxCharacters = DefaultMaxCharacters)
    {
        using var reader = XmlReader.Create(path, Settings(maxCharacters));
        return XDocument.Load(reader, options);
    }

    /// <summary>False (and null) instead of an exception when the text is not acceptable XML.</summary>
    public static bool TryParse(string? xml, [NotNullWhen(true)] out XDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try { doc = Parse(xml); return doc.Root is not null; }
        catch (XmlException) { doc = null; return false; }
    }
}
