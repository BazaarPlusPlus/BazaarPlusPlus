#nullable enable
namespace BazaarPlusPlus.Game.BundlePipeline;

internal interface IBundleOutboxFiles
{
    Stream OpenRead(string fileName);

    bool Exists(string fileName);

    long GetLength(string fileName);

    void Delete(string fileName);

    IReadOnlyList<string> EnumerateBundleFileNames();
}

internal sealed class SystemBundleOutboxFiles : IBundleOutboxFiles
{
    private readonly string _root;

    internal SystemBundleOutboxFiles(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public Stream OpenRead(string fileName) =>
        new FileStream(Resolve(fileName), FileMode.Open, FileAccess.Read, FileShare.Read);

    public bool Exists(string fileName) => File.Exists(Resolve(fileName));

    public long GetLength(string fileName) => new FileInfo(Resolve(fileName)).Length;

    public void Delete(string fileName) => File.Delete(Resolve(fileName));

    public IReadOnlyList<string> EnumerateBundleFileNames() =>
        Directory
            .EnumerateFiles(_root, "*.bundle")
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Select(name => name!)
            .ToArray();

    private string Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new InvalidDataException("bundle_file_name_invalid");
        return Path.Combine(_root, fileName);
    }
}
