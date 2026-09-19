using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace RuntimeIntegration.Tests;

public sealed class SceneRuntimeCompatibilityTests
{
    [Fact]
    public void Compiled_mod_does_not_bind_to_the_version_specific_scene_handle_getter()
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "BazaarPlusPlus.dll")
        );
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var sceneMembers = new List<string>();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
                continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (
                metadata.GetString(type.Namespace) == "UnityEngine.SceneManagement"
                && metadata.GetString(type.Name) == "Scene"
            )
                sceneMembers.Add(metadata.GetString(member.Name));
        }

        Assert.DoesNotContain("get_handle", sceneMembers);
        Assert.Contains("op_Equality", sceneMembers);
    }
}
