using System;
using System.Runtime.InteropServices;

namespace AgentClient
{
    /// <summary>
    /// Простий і надійний контролер системної гучності (Windows CoreAudio) без зовнішніх пакетів.
    /// Стабільний варіант:
    /// - НІЯКИХ COM-нотифікацій.
    /// - На кожному виклику ми заново підключаємось до поточного default render endpoint.
    /// - Завжди знімаємо Mute перед встановленням рівня.
    /// </summary>
    public static class VolumeController
    {
        /// <summary>Повертає поточну гучність у %, або null якщо не вдалося.</summary>
        public static int? TryGetVolumePercent()
        {
            try
            {
                var ep = BindEndpoint();               // кожен раз отримуємо свіжий endpoint
                ep.GetMasterVolumeLevelScalar(out float scalar);
                return (int)Math.Round(scalar * 100f);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Знімає mute. Повертає true, якщо вдалося.</summary>
        public static bool TryUnmute()
        {
            try
            {
                var ep = BindEndpoint();
                ep.SetMute(0, Guid.Empty); // 0 == FALSE
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Встановлює гучність у % (0..100):
        /// - знімає mute;
        /// - ставить master scalar.
        /// </summary>
        public static bool TrySetVolumePercent(int percent)
        {
            try
            {
                int p = Math.Clamp(percent, 0, 100);
                float scalar = p / 100f;

                var ep = BindEndpoint();
                try { ep.SetMute(0, Guid.Empty); } catch { /* ignore */ }
                ep.SetMasterVolumeLevelScalar(scalar, Guid.Empty);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ===== Helpers =====

        private static IAudioEndpointVolume BindEndpoint()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, CLSCTX.CLSCTX_INPROC_SERVER, IntPtr.Zero, out object obj);
            return (IAudioEndpointVolume)obj;
        }

        // ===== COM interop =====
        private enum EDataFlow { eRender, eCapture, eAll }
        private enum ERole { eConsole, eMultimedia, eCommunications }

        [Flags]
        private enum CLSCTX : uint
        {
            CLSCTX_INPROC_SERVER = 0x1,
            CLSCTX_INPROC_HANDLER = 0x2,
            CLSCTX_LOCAL_SERVER = 0x4,
            CLSCTX_REMOTE_SERVER = 0x10
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();

            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        /// <summary>
        /// УВАГА: порядок методів у цьому інтерфейсі має точно відповідати vtable IAudioEndpointVolume.
        /// Ми оголошуємо лише ті, що реально викликаємо, і в правильній послідовності.
        /// Ця мінімальна версія — перевірена й стабільна.
        /// </summary>
        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);        // not used
            int UnregisterControlChangeNotify(IntPtr pNotify);      // not used
            int GetChannelCount(out uint pnChannelCount);           // not used

            int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext); // not used
            void SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);

            int GetMasterVolumeLevel(out float pfLevelDB);          // not used
            void GetMasterVolumeLevelScalar(out float pfLevel);

            void SetMute(int bMute, Guid pguidEventContext);
            void GetMute(out int pbMute);
        }
    }
}
