using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if __ANDROID__
namespace MelonLoader
{
    internal static class bHaptics_NativeLibrary
    {
        public static Action OnChange = () => { };

        public static void SubmitPath(string key, string position, float[] x, float[] y, int[] intensities, int duration)
        {
            Internal_SubmitPath(key, position, x, y, intensities, new []{ x.Length, y.Length, intensities.Length }, duration);
        }

        public static void SubmitDot(string key, bHaptics.PositionType position, int[] indexes, int[] intensities, int duration)
        {
            if (indexes == null) throw new ArgumentNullException(nameof(indexes));
            if (intensities == null) throw new ArgumentNullException(nameof(intensities));

            // The native call consumes the arrays synchronously. Pin them for the
            // call instead of depending on a net48-only UnmanagedArray assembly.
            unsafe
            {
                fixed (int* nativeIndexes = indexes)
                fixed (int* nativeIntensities = intensities)
                {
                    Internal_SubmitDot(key, position.ToString(), (IntPtr)nativeIndexes, indexes.Length,
                        (IntPtr)nativeIntensities, intensities.Length, duration);
                }
            }
        }

        public static byte[] GetPositionStatus(bHaptics.PositionType position)
        {
            return NativeParser.ParseBytesArray(Internal_GetPositionStatus(position.ToString()));
        }
        
        private static void Invoke_OnChange()
        {
            OnChange.Invoke();
        }

        
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void Start();
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void Stop();
        
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void TurnOff([MarshalAs(UnmanagedType.LPStr)] string key);
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void TurnOffAll();
        
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void RegisterProject([MarshalAs(UnmanagedType.LPStr)] string key,
            [MarshalAs(UnmanagedType.LPStr)] string contents);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void RegisterProjectReflected([MarshalAs(UnmanagedType.LPStr)] string key,
            [MarshalAs(UnmanagedType.LPStr)] string contents);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void SubmitRegistered([MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string altKey, float intensity, float duration, float offsetAngleX, float offsetY);
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static void SubmitRegisteredWithTime([MarshalAs(UnmanagedType.LPStr)] string key, int startTime);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static bool IsRegistered([MarshalAs(UnmanagedType.LPStr)] string key);
        
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static bool IsPlaying([MarshalAs(UnmanagedType.LPStr)] string key);
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        public extern static bool IsAnythingPlaying();
        
        // sizes is the size of each array
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        private extern static void Internal_SubmitDot([MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string position, IntPtr indexes, int index_size, IntPtr intensities, int intensity_size, int duration);
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        private extern static void Internal_SubmitPath([MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string position, float[] x, float[] y, int[] intensities, int[] sizes, int duration);
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Native)]
        private extern static IntPtr Internal_GetPositionStatus(string position);
    }
}
#endif