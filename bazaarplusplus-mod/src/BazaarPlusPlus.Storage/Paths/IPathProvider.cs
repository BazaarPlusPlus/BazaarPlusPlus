#nullable enable
namespace BazaarPlusPlus.Storage.Paths;

public interface IPathProvider
{
    string? DataRootDirectoryPath { get; }

    string? PluginsDirectoryPath { get; }
}
