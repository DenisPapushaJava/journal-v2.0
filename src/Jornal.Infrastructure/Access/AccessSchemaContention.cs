using System.Data.OleDb;
using System.Runtime.Versioning;

namespace Jornal.Infrastructure.Access;

internal static class AccessSchemaContention
{
    [SupportedOSPlatform("windows")]
    internal static bool IsBusy(Exception exception) => exception switch
    {
        OleDbException provider => IsProviderLock(provider.HResult,
            provider.Errors.Cast<OleDbError>().Select(error => error.NativeError).ToArray()),
        IOException io => IsLockResult(io.HResult),
        _ => false
    };

    internal static bool IsProviderLock(int hresult, IReadOnlyCollection<int> nativeErrors)
    {
        if (hresult is unchecked((int)0x80070005) or unchecked((int)0x80030005)
            or unchecked((int)0x80040E09) or unchecked((int)0x80040E4D) or unchecked((int)0x80040E94)) return false;
        // Не анализируем локализованный текст. Неоднозначные 3050/3051 могут
        // означать отсутствие прав; без явного кода блокировки их не повторяем.
        var codes = nativeErrors.Where(code => code != 0).ToArray();
        if (codes.Length > 0) return codes.All(code => code is 3008 or 3045 or 3046 or 3188 or 3211 or 3218 or 3260 or 3261 or 3262);
        return IsLockResult(hresult);
    }
    private static bool IsLockResult(int hresult) => hresult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
            or unchecked((int)0x80030020) or unchecked((int)0x80030021)
            or unchecked((int)0x80040E40) or unchecked((int)0x80040E92);
}
