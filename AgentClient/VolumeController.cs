using System;
using System.Runtime.InteropServices;

namespace AgentClient
{
    /// <summary>
    /// Контролер системної гучності (Windows CoreAudio) без зовнішніх пакетів.
    /// Стабільний і «анти-моно»: при установці рівня нормалізуємо всі канали (L/R).
    /// - Без нотифікацій.
    /// - На кожен виклик перепідключаємося до актуального default render endpoint.
    /// - Завжди знімаємо mute перед установкою.
    /// </summary>
    public static class VolumeController
    {
        /// <summary>Повертає поточну гучність у %, або null якщо не вдалося.</summary>
        public static int? TryGetVolumePercent()
        {
            try
            {
                var ep = BindEndpoint();
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
            catch { return false; }
        }

        /// <summary>
        /// Встановлює гучність у % (0..100) і вирівнює канали:
        /// - знімає mute;
        /// - ставить master scalar;
        /// - ставить channel scalar для кожного каналу (якщо їх > 1).
        /// </summary>
        public static bool TrySetVolumePercent(int percent)
        {
            try
            {
                int p = Math.Clamp(percent, 0, 100);
                float scalar = p / 100f;

                var ep = BindEndpoint();

                // Знімаємо mute перед застосуванням
                try { ep.SetMute(0, Guid.Empty); } catch { /* ignore */ }

                // Master
                ep.SetMasterVolumeLevelScalar(scalar, Guid.Empty);

                // Канали (ліва/права й інші, якщо є)
                try
                {
                    ep.GetChannelCount(out uint chCount);
                    if (chCount > 1)
                    {
                        for (uint ch = 0; ch < chCount; ch++)
                            ep.SetChannelVolumeLevelScalar(scalar, ch, Guid.Empty);
                    }
                }
                catch { /* каналів може не бути або інтерфейс забороняє — ігноруємо */ }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Нормалізує баланс каналів під поточний master (ставить однаковий scalar на всі канали).
        /// Корисно, якщо правий/лівий «провисає» при незмінному master.
        /// </summary>
        public static bool TryNormalizeChannels()
        {
            try
            {
                var ep = BindEndpoint();
                ep.GetMasterVolumeLevelScalar(out float scalar);
                ep.GetChannelCount(out uint chCount);
                if (chCount > 1)
                {
                    for (uint ch = 0; ch < chCount; ch++)
                        ep.SetChannelVolumeLevelScalar(scalar, ch, Guid.Empty);
                }
                return true;
            }
            catch { return false; }
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
        /// ВАЖЛИВО: порядок методів повинен відповідати фактичному порядку у vtable IAudioEndpointVolume.
        /// Тому тут наведено послідовність згідно з CoreAudio API (без пропусків до використовуваних нами методів).
        /// </summary>
        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            // 1–7: master + базове
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
            void SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            void GetMasterVolumeLevelScalar(out float pfLevel);

            // 8–11: канали (нам потрібні Scalar-set і Get)
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
            void SetChannelVolumeLevelScalar(float fLevel, uint nChannel, Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            void GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

            // 12–18: mute/step/range (використовуємо лише mute)
            void SetMute(int bMute, Guid pguidEventContext);
            void GetMute(out int pbMute);
            int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
            int VolumeStepUp(Guid pguidEventContext);
            int VolumeStepDown(Guid pguidEventContext);
            int QueryHardwareSupport(out uint pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
        }
    }
}
