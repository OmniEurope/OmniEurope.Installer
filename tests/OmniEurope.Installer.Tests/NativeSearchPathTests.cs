using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using OmniEurope.Installer.Msi;
using Shouldly;

namespace OmniEurope.Installer.Tests;

/// <summary>msi.dll and the other native imports load from System32 only, never from a planted copy next to the program.</summary>
public sealed class NativeSearchPathTests
{
    private const string AttributeName = nameof(DefaultDllImportSearchPathsAttribute);

    [Fact]
    public void Every_msi_import_of_the_library_searches_System32_only()
    {
        MethodInfo[] imports = typeof(MsiNative)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .ToArray();

        imports.ShouldNotBeEmpty();
        DllImportSearchPath? assemblyPaths = typeof(MsiNative).Assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths;
        foreach (MethodInfo import in imports)
        {
            (import.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths ?? assemblyPaths)
                .ShouldBe(DllImportSearchPath.System32, import.Name);
        }
    }

    [Fact]
    public void The_setup_host_imports_search_System32_only()
    {
        string host = typeof(NativeSearchPathTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "SetupHost").Value!;
        using var stream = File.OpenRead(host);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();

        SearchPaths(metadata, metadata.GetAssemblyDefinition().GetCustomAttributes()).ShouldBe([(int)DllImportSearchPath.System32]);
        foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
        {
            MethodDefinition method = metadata.GetMethodDefinition(handle);
            if (method.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            {
                SearchPaths(metadata, method.GetCustomAttributes())
                    .ShouldAllBe(paths => paths == (int)DllImportSearchPath.System32, metadata.GetString(method.Name));
            }
        }
    }

    /// <summary>The values of the search-path attributes in a list (blob: prolog 0x0001, then the int32 enum value).</summary>
    private static int[] SearchPaths(MetadataReader metadata, CustomAttributeHandleCollection attributes) =>
        attributes.Select(metadata.GetCustomAttribute)
            .Where(attribute => AttributeTypeName(metadata, attribute) == AttributeName)
            .Select(attribute =>
            {
                BlobReader blob = metadata.GetBlobReader(attribute.Value);
                blob.ReadUInt16();
                return blob.ReadInt32();
            })
            .ToArray();

    private static string? AttributeTypeName(MetadataReader metadata, CustomAttribute attribute) =>
        attribute.Constructor.Kind == HandleKind.MemberReference
        && metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
            ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name)
            : null;
}
