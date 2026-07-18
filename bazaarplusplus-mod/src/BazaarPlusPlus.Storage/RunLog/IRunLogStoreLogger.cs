#nullable enable
namespace BazaarPlusPlus.Storage.RunLog;

public interface IRunLogStoreLogger
{
    void Emit(RunLogStoreDiagnostic diagnostic);
}
