#if ANDROID
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MelonLoader.Bootstrap;

// Called by the JNI shim only after the Java side has staged the inputs.
internal static class AndroidEntry
{
    private static int initialized;

    [UnmanagedCallersOnly(EntryPoint = "MelonLoaderAndroidInitialize", CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe int Initialize(nint bootstrap, nint unity, byte* basePath)
    {
        if (bootstrap == 0 || unity == 0 || basePath == null)
            return 0;
        if (Interlocked.CompareExchange(ref initialized, 1, 0) != 0)
            return 0;
        try
        {
            string? path = Marshal.PtrToStringUTF8((nint)basePath);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                throw new DirectoryNotFoundException(path);
            Exports.InitializeAndroid(bootstrap, unity, path);
            Core.Init();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MelonLoader Android bootstrap failed: {ex}");
            // Do not retry after partially installing hooks.
            return 0;
        }
    }
}
#endif
