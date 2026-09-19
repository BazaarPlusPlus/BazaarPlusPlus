#nullable enable
namespace BazaarPlusPlus.TestSupport;

internal sealed class EditorConfigSection
{
    internal EditorConfigSection(
        string header,
        IReadOnlyDictionary<string, string> properties,
        string sourcePath
    )
    {
        Header = header;
        Properties = properties;
        SourcePath = sourcePath;
    }

    internal string Header { get; }

    internal IReadOnlyDictionary<string, string> Properties { get; }

    internal string SourcePath { get; }
}
