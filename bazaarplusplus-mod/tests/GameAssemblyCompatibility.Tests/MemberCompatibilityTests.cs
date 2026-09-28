using System.Reflection;
using System.Runtime.Loader;
using BazaarPlusPlus.GameAssemblyCompatibility;
using Xunit;

namespace GameAssemblyCompatibility.Tests;

public sealed class MemberCompatibilityTests
{
    [Theory]
    [InlineData("params")]
    [InlineData("generic")]
    [InlineData("field_generic")]
    [InlineData("field")]
    public void Existing_exact_members_pass(string shape)
    {
        using var files = new MetadataFixtures();
        var result = MemberCompatibility.Check(
            files.Library("runtime.dll", shape),
            [files.Consumer("consumer.dll", shape)]
        );
        Assert.Empty(result.Failures);
        Assert.True(result.Members > 0);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("parameter")]
    [InlineData("return")]
    [InlineData("static")]
    [InlineData("byref")]
    [InlineData("array_rank")]
    [InlineData("generic_arity")]
    public void Same_name_is_not_enough_to_link_a_method(string shape)
    {
        using var files = new MetadataFixtures();
        var result = MemberCompatibility.Check(
            files.Library("runtime.dll"),
            [files.Consumer("consumer.dll", shape)]
        );
        Assert.Contains("::Call", Assert.Single(result.Failures));
    }

    [Fact]
    public void Field_type_is_part_of_its_signature()
    {
        using var files = new MetadataFixtures();
        var result = MemberCompatibility.Check(
            files.Library("runtime.dll", "field"),
            [files.Consumer("consumer.dll", "field_type")]
        );
        Assert.Contains("::Value", Assert.Single(result.Failures));
    }

    [Fact]
    public void A_type_reference_without_a_member_is_still_checked()
    {
        using var files = new MetadataFixtures();
        var result = MemberCompatibility.Check(
            files.Library("runtime.dll", "missing_type"),
            [files.Consumer("consumer.dll", typeOnly: true)]
        );
        Assert.Equal(0, result.Members);
        Assert.Contains("Fixture.Token", Assert.Single(result.Failures));
    }

    [Fact]
    public void Generic_base_members_are_resolved_with_substitution()
    {
        using var files = new MetadataFixtures();
        var result = MemberCompatibility.Check(
            files.Library("runtime.dll", "generic_base"),
            [files.Consumer("consumer.dll", "derived_generic")]
        );
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData("generic", "generic_concrete", "::Call")]
    [InlineData("field_generic", "field_generic_concrete", "::Value")]
    public void Generic_slot_does_not_match_a_concrete_signature_on_a_closed_type(
        string reference,
        string runtime,
        string diagnostic
    )
    {
        using var files = new MetadataFixtures();
        var runtimePath = files.Library("runtime.dll", runtime);
        var consumerPath = files.Consumer("consumer.dll", reference);
        var result = MemberCompatibility.Check(runtimePath, [consumerPath]);
        Assert.Contains(diagnostic, Assert.Single(result.Failures));

        var context = new AssemblyLoadContext(null, isCollectible: true);
        Assembly Load(string path)
        {
            using var stream = File.OpenRead(path);
            return context.LoadFromStream(stream);
        }
        context.Resolving += (_, name) => name.Name == "Fixture.Json" ? Load(runtimePath) : null;
        try
        {
            var consumer = Load(consumerPath);
            var error = Assert.Throws<TargetInvocationException>(() =>
                consumer.GetType("Consumer")!.GetMethod("Use")!.Invoke(null, null)
            );
            Assert.Equal(
                reference == "generic"
                    ? typeof(MissingMethodException)
                    : typeof(MissingFieldException),
                error.InnerException!.GetType()
            );
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Constructors_are_not_inherited()
    {
        using var files = new MetadataFixtures();
        var result = MemberCompatibility.Check(
            files.Library("runtime.dll", "base_ctor"),
            [files.Consumer("consumer.dll", "derived_ctor")]
        );
        Assert.Contains("::.ctor", Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("type", false)]
    [InlineData("readonly", false)]
    [InlineData("missing", true)]
    [InlineData("type", true)]
    [InlineData("readonly", true)]
    public void Attribute_named_members_are_checked_even_when_constructor_exists(
        string shape,
        bool field
    )
    {
        using var files = new MetadataFixtures();
        var (runtime, consumer) = files.Attribute(shape, field);
        var result = MemberCompatibility.Check(runtime, [consumer]);
        Assert.Equal(1, result.NamedArguments);
        Assert.Contains(
            $"::Order attribute {(field ? "Field" : "Property")}",
            Assert.Single(result.Failures)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_attribute_named_member_passes(bool field)
    {
        using var files = new MetadataFixtures();
        var (runtime, consumer) = files.Attribute("valid", field);
        var result = MemberCompatibility.Check(runtime, [consumer]);
        Assert.Equal(1, result.NamedArguments);
        Assert.Empty(result.Failures);
    }
}
