using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace LogAnalyzer.Edition.Tests;

/// <summary>What a managed assembly references and contains, read from metadata only (no code is loaded or run).</summary>
public sealed record AssemblyFacts(
    string File,
    IReadOnlySet<string> AssemblyRefs,
    IReadOnlySet<string> TypeRefs,          // Namespace.Name of referenced types
    IReadOnlySet<string> MemberRefs,        // Namespace.Type::Member
    IReadOnlySet<string> DefinedTypes,      // Namespace.Name of types defined here
    IReadOnlyList<(string Dll, string Function)> PInvokes,
    IReadOnlySet<string> UserStrings);

public static class BuildOutputInspector
{
    public static string Output(string key) =>
        typeof(BuildOutputInspector).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;

    /// <summary>Assemblies built from this repository (LogAnalyzer*.dll). Third-party and framework assemblies are listed separately.</summary>
    public static IEnumerable<string> OwnAssemblies(string dir) =>
        Directory.EnumerateFiles(dir, "LogAnalyzer*.dll").Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));

    public static AssemblyFacts Read(string path)
    {
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        // A native PE (apphost .exe, native dll) has no metadata; callers skip it via BadImageFormatException.
        if (!pe.HasMetadata) throw new BadImageFormatException("not a managed assembly", path);
        var md = pe.GetMetadataReader();
        string Name(StringHandle h) => md.GetString(h);

        var asmRefs = md.AssemblyReferences.Select(h => Name(md.GetAssemblyReference(h).Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        string TypeName(EntityHandle h) => h.Kind switch
        {
            HandleKind.TypeReference => Full(md.GetTypeReference((TypeReferenceHandle)h)),
            HandleKind.TypeDefinition => FullDef(md.GetTypeDefinition((TypeDefinitionHandle)h)),
            HandleKind.TypeSpecification => "<spec>",
            _ => "<other>",
        };
        string Full(TypeReference t) => t.Namespace.IsNil ? Name(t.Name) : Name(t.Namespace) + "." + Name(t.Name);
        string FullDef(TypeDefinition t) => t.Namespace.IsNil ? Name(t.Name) : Name(t.Namespace) + "." + Name(t.Name);

        var typeRefs = md.TypeReferences.Select(h => Full(md.GetTypeReference(h))).ToHashSet();
        var defined = md.TypeDefinitions.Select(h => FullDef(md.GetTypeDefinition(h))).ToHashSet();
        var members = new HashSet<string>();
        foreach (var h in md.MemberReferences)
        {
            var m = md.GetMemberReference(h);
            members.Add($"{TypeName(m.Parent)}::{Name(m.Name)}");
        }
        var pinvokes = new List<(string, string)>();
        foreach (var h in md.MethodDefinitions)
        {
            var imp = md.GetMethodDefinition(h).GetImport();
            if (imp.Module.IsNil) continue;
            pinvokes.Add((Name(md.GetModuleReference(imp.Module).Name), Name(imp.Name)));
        }
        var strings = new HashSet<string>();
        int size = md.GetHeapSize(HeapIndex.UserString);
        for (var h = MetadataTokens.UserStringHandle(1); !h.IsNil;)
        {
            string s;
            try { s = md.GetUserString(h); } catch (BadImageFormatException) { break; }
            strings.Add(s);
            int next = md.GetNextHandle(h) is { IsNil: false } n ? MetadataTokens.GetHeapOffset(n) : 0;
            if (next == 0 || next >= size) break;
            h = MetadataTokens.UserStringHandle(next);
        }
        return new AssemblyFacts(Path.GetFileName(path), asmRefs, typeRefs, members, defined, pinvokes, strings);
    }
}
