using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.Bootstrap.Logging;

namespace MelonLoader.Bootstrap;

internal static partial class PltHook
{
    [LibraryImport("*", EntryPoint = "plthook_open", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int PlthookOpen(ref nint pltHookOut, string? filename);

    [LibraryImport("*", EntryPoint = "plthook_open_by_handle")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int PlthookOpenByHandle(ref nint pltHookOut, nint handle);

    [LibraryImport("*", EntryPoint = "plthook_open_by_address")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int PlthookOpenByAddress(ref nint pltHookOut, nint address);

    [LibraryImport("*", EntryPoint = "plthook_replace", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int PlthookReplace(nint pltHook, string funcName, nint funcAddr, nint oldFunc);

    [LibraryImport("*", EntryPoint = "plthook_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void PlthookClose(nint pltHook);

    [LibraryImport("*", EntryPoint = "plthook_enum", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int PlthookEnum(nint pltHook, ref uint pos, byte** name, byte*** addr);

    [LibraryImport("*", EntryPoint = "plthook_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint PlthookError();

#if !ANDROID
    internal static readonly string? PlayerFileName = Process.GetCurrentProcess().Modules.OfType<ProcessModule>()
        .FirstOrDefault(x => x.FileName.Contains("UnityPlayer"))?.FileName;
#endif

    internal static bool InstallHooks(List<(string functionName, nint hookFunctionPtr)> hooks)
    {
        nint pltHook = IntPtr.Zero;
        bool pltHookOpened = false;

#if ANDROID
        pltHookOpened = PlthookOpenByHandle(ref pltHook, Exports.UnityLibraryHandle) == 0;
#elif OSX
        string parentPlayerPath = Path.GetDirectoryName(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName))!;
        string playerLibPath = Path.Combine(parentPlayerPath, "Frameworks", "UnityPlayer.dylib");
        if (File.Exists(playerLibPath))
        {
            nint libHandle = LibcNative.Dlopen(playerLibPath, LibcNative.RtldLazy | LibcNative.RtldNoLoad);
            if (libHandle == IntPtr.Zero)
            {
                Console.WriteLine($"Failed to dlopen {playerLibPath}, cannot apply plt hooks");
                return false;
            }
            pltHookOpened = PlthookOpenByHandle(ref pltHook, libHandle) == 0;
        }
        else
        {
            pltHookOpened = PlthookOpen(ref pltHook, PlayerFileName) == 0;
        }
#else
        pltHookOpened = PlthookOpen(ref pltHook, PlayerFileName) == 0;
#endif

        if (!pltHookOpened)
        {
            MelonLogger.LogError($"plthook_open error: {Marshal.PtrToStringAuto(PlthookError())}");
            return false;
        }

        bool success = true;
        foreach (var hook in hooks)
        {
            if (PlthookReplace(pltHook, hook.functionName, hook.hookFunctionPtr, IntPtr.Zero) != 0)
            {
                MelonDebug.Log($"plthook_replace error when hooking {hook.functionName}: " +
                               $"{Marshal.PtrToStringAuto(PlthookError())}");
                success = false;
                continue;
            }

            MelonDebug.Log($"Plt hooked {hook.functionName} successfully");
        }

        PlthookClose(pltHook);
        return success;
    }
}