using System.Text;
using BazaarPlusPlus.Game.VoiceSubtitles;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: VoiceLinesValidator <voice-lines.json>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Voice line JSON does not exist: {path}");
    return 1;
}

try
{
    var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
    var result = VoiceLinesValidationCore.Parse(json, "local-build");
    Console.WriteLine($"Validated {result.Lines.Length} voice lines from {path}.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Invalid voice line JSON '{path}': {ex.Message}");
    return 1;
}
