using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SesKalkani
{
    // ---------------------------------------------------------------- Windows Core Audio (WASAPI) arayüzleri
    static class CoreAudio
    {
        public static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        public static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        public static readonly Guid IID_IAudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
        public static readonly Guid SUB_PCM = new Guid("00000001-0000-0010-8000-00aa00389b71");
        public static readonly Guid SUB_FLOAT = new Guid("00000003-0000-0010-8000-00aa00389b71");

        public const int eRender = 0, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 0x17;
        public const int SHAREMODE_EXCLUSIVE = 1, STREAMFLAGS_EVENTCALLBACK = 0x40000, BUFFERFLAGS_SILENT = 2;
        public const int E_BUFFER_SIZE_NOT_ALIGNED = unchecked((int)0x88890019);
        public const int E_DEVICE_IN_USE = unchecked((int)0x8889000A);
        public const int E_EXCLUSIVE_NOT_ALLOWED = unchecked((int)0x8889000E);
        public const int E_UNSUPPORTED_FORMAT = unchecked((int)0x88890008);

        [StructLayout(LayoutKind.Sequential)]
        public struct PROPERTYKEY { public Guid fmtid; public int pid; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p, p2; }

        public static PROPERTYKEY PKEY_FriendlyName = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT pv);
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] public static extern IntPtr AvSetMmThreadCharacteristics(string task, ref int index);
        [DllImport("avrt.dll")] public static extern bool AvRevertMmThreadCharacteristics(IntPtr h);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out CoreAudio.PROPERTYKEY key);
        [PreserveSig] int GetValue(ref CoreAudio.PROPERTYKEY key, out CoreAudio.PROPVARIANT value);
        [PreserveSig] int SetValue(ref CoreAudio.PROPERTYKEY key, ref CoreAudio.PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out int frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out int padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(int frames, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(int frames, int flags);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    // ---------------------------------------------------------------- Özel mod çıkış
    /// <summary>
    /// Sanal kabloya sesi WASAPI "özel mod" ile yazar. Özel mod Windows ses karıştırıcısını atladığı için
    /// Discord'un yayın (ekran paylaşımı) ses yakalaması bu sesi göremez; böylece mikrofonun yayına karışmaz.
    /// </summary>
    public class WasapiExclusiveOut
    {
        public SampleFifo Fifo;
        public string FormatInfo = "";
        volatile bool run;
        Thread th;
        string initError;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        public volatile string Error;

        struct Fmt { public int Rate, Ch, Bits, Valid; public bool Float; }

        /// <summary>Başlatır. Başarısızsa hata metni döner.</summary>
        public string Start(string deviceNameStart)
        {
            run = true; initError = null; ready.Reset();
            th = new Thread(() => Run(deviceNameStart)) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "OzelModCikis" };
            th.SetApartmentState(ApartmentState.MTA);
            th.Start();
            if (!ready.WaitOne(5000)) { run = false; return "Sanal mikrofon yanıt vermedi."; }
            if (initError != null) { run = false; th.Join(1000); return initError; }
            return null;
        }

        public void Stop()
        {
            run = false;
            if (th != null) th.Join(1500);
            th = null;
        }

        void Fail(string e) { initError = e; ready.Set(); }

        static string FriendlyName(IMMDevice d)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0, out ps) != 0 || ps == null) return "";
            var pv = new CoreAudio.PROPVARIANT();
            string name = "";
            if (ps.GetValue(ref CoreAudio.PKEY_FriendlyName, out pv) == 0 && pv.vt == 31 && pv.p != IntPtr.Zero)
                name = Marshal.PtrToStringUni(pv.p);
            CoreAudio.PropVariantClear(ref pv);
            Marshal.ReleaseComObject(ps);
            return name ?? "";
        }

        static IMMDevice FindDevice(string nameStart)
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDeviceCollection col;
            if (en.EnumAudioEndpoints(CoreAudio.eRender, CoreAudio.DEVICE_STATE_ACTIVE, out col) != 0) return null;
            int n; col.GetCount(out n);
            IMMDevice best = null;
            for (int i = 0; i < n; i++)
            {
                IMMDevice d; if (col.Item(i, out d) != 0) continue;
                string fn = FriendlyName(d);
                // waveOut adları 31 karakterle kesilir; başlangıcı eşleşen cihazı bul
                if (nameStart.Length > 0 && fn.StartsWith(nameStart, StringComparison.OrdinalIgnoreCase)) { best = d; break; }
                Marshal.ReleaseComObject(d);
            }
            return best;
        }

        static IntPtr MakeFormat(Fmt f)
        {
            IntPtr p = Marshal.AllocCoTaskMem(40);
            int block = f.Ch * f.Bits / 8;
            Marshal.WriteInt16(p, 0, unchecked((short)0xFFFE));       // WAVE_FORMAT_EXTENSIBLE
            Marshal.WriteInt16(p, 2, (short)f.Ch);
            Marshal.WriteInt32(p, 4, f.Rate);
            Marshal.WriteInt32(p, 8, f.Rate * block);
            Marshal.WriteInt16(p, 12, (short)block);
            Marshal.WriteInt16(p, 14, (short)f.Bits);
            Marshal.WriteInt16(p, 16, 22);                             // cbSize
            Marshal.WriteInt16(p, 18, (short)f.Valid);
            Marshal.WriteInt32(p, 20, f.Ch == 1 ? 0x4 : 0x3);          // orta / sol+sağ
            byte[] g = (f.Float ? CoreAudio.SUB_FLOAT : CoreAudio.SUB_PCM).ToByteArray();
            Marshal.Copy(g, 0, p + 24, 16);
            return p;
        }

        void Run(string nameStart)
        {
            IMMDevice dev = null; IAudioClient ac = null; IAudioRenderClient rc = null;
            IntPtr fmtPtr = IntPtr.Zero, mmcss = IntPtr.Zero;
            var ev = new AutoResetEvent(false);
            try
            {
                dev = FindDevice(nameStart);
                if (dev == null) { Fail("Sanal mikrofon (CABLE Input) bulunamadı."); return; }

                Guid iidAc = CoreAudio.IID_IAudioClient;
                object o;
                if (dev.Activate(ref iidAc, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out o) != 0) { Fail("Sanal mikrofon açılamadı."); return; }
                ac = (IAudioClient)o;

                // Desteklenen bir format bul
                var cands = new System.Collections.Generic.List<Fmt>();
                foreach (int rate in new[] { 48000, 44100, 96000 })
                    foreach (int ch in new[] { 2, 1 })
                    {
                        cands.Add(new Fmt { Rate = rate, Ch = ch, Bits = 16, Valid = 16 });
                        cands.Add(new Fmt { Rate = rate, Ch = ch, Bits = 24, Valid = 24 });
                        cands.Add(new Fmt { Rate = rate, Ch = ch, Bits = 32, Valid = 24 });
                        cands.Add(new Fmt { Rate = rate, Ch = ch, Bits = 32, Valid = 32, Float = true });
                        cands.Add(new Fmt { Rate = rate, Ch = ch, Bits = 32, Valid = 32 });
                    }
                Fmt fmt = new Fmt(); bool found = false;
                foreach (var c in cands)
                {
                    IntPtr p = MakeFormat(c), closest;
                    int hr = ac.IsFormatSupported(CoreAudio.SHAREMODE_EXCLUSIVE, p, out closest);
                    if (closest != IntPtr.Zero) Marshal.FreeCoTaskMem(closest);
                    if (hr == 0) { fmt = c; fmtPtr = p; found = true; break; }
                    Marshal.FreeCoTaskMem(p);
                    if (hr == CoreAudio.E_EXCLUSIVE_NOT_ALLOWED) { Fail("Windows, sanal mikrofonda özel moda izin vermiyor."); return; }
                }
                if (!found) { Fail("Sanal mikrofon özel modda uygun bir format kabul etmedi."); return; }

                long defP, minP; ac.GetDevicePeriod(out defP, out minP);
                long period = Math.Max(defP, minP);
                int hrInit = ac.Initialize(CoreAudio.SHAREMODE_EXCLUSIVE, CoreAudio.STREAMFLAGS_EVENTCALLBACK, period, period, fmtPtr, IntPtr.Zero);
                if (hrInit == CoreAudio.E_BUFFER_SIZE_NOT_ALIGNED)
                {
                    int fr; ac.GetBufferSize(out fr);
                    period = (long)(10000000.0 * fr / fmt.Rate + 0.5);
                    Marshal.ReleaseComObject(ac); ac = null;
                    if (dev.Activate(ref iidAc, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out o) != 0) { Fail("Sanal mikrofon açılamadı."); return; }
                    ac = (IAudioClient)o;
                    hrInit = ac.Initialize(CoreAudio.SHAREMODE_EXCLUSIVE, CoreAudio.STREAMFLAGS_EVENTCALLBACK, period, period, fmtPtr, IntPtr.Zero);
                }
                if (hrInit == CoreAudio.E_DEVICE_IN_USE) { Fail("Sanal mikrofonu başka bir program kullanıyor."); return; }
                if (hrInit == CoreAudio.E_EXCLUSIVE_NOT_ALLOWED) { Fail("Windows, sanal mikrofonda özel moda izin vermiyor."); return; }
                if (hrInit != 0) { Fail("Özel mod başlatılamadı (0x" + hrInit.ToString("X8") + ")."); return; }

                if (ac.SetEventHandle(ev.SafeWaitHandle.DangerousGetHandle()) != 0) { Fail("Özel mod olayı ayarlanamadı."); return; }
                int frames; ac.GetBufferSize(out frames);
                Guid iidRc = CoreAudio.IID_IAudioRenderClient;
                if (ac.GetService(ref iidRc, out o) != 0) { Fail("Özel mod çıkışı alınamadı."); return; }
                rc = (IAudioRenderClient)o;

                int bufMs = (int)(frames * 1000L / fmt.Rate);
                Fifo = new SampleFifo(fmt.Rate, Math.Max(20, bufMs + 10));
                FormatInfo = fmt.Rate + " Hz, " + fmt.Ch + " kanal, " + fmt.Bits + " bit" + (fmt.Float ? " float" : "") + ", tampon " + bufMs + " ms";

                // İlk tamponu sessizlikle doldur, sonra başlat
                IntPtr data;
                if (rc.GetBuffer(frames, out data) == 0) rc.ReleaseBuffer(frames, CoreAudio.BUFFERFLAGS_SILENT);
                if (ac.Start() != 0) { Fail("Özel mod çıkışı başlatılamadı."); return; }

                try { int idx = 0; mmcss = CoreAudio.AvSetMmThreadCharacteristics("Pro Audio", ref idx); } catch { }
                ready.Set();

                var tmp = new float[frames];
                int bps = fmt.Bits / 8;
                while (run)
                {
                    if (!ev.WaitOne(500)) continue;
                    if (rc.GetBuffer(frames, out data) != 0) continue;
                    Fifo.Read(tmp, frames);
                    WriteSamples(data, tmp, frames, fmt, bps);
                    rc.ReleaseBuffer(frames, 0);
                }
                ac.Stop();
            }
            catch (Exception e)
            {
                if (!ready.WaitOne(0)) Fail("Özel mod hatası: " + e.Message);
                else Error = "Sanal mikrofon hatası: " + e.Message;
            }
            finally
            {
                if (mmcss != IntPtr.Zero) try { CoreAudio.AvRevertMmThreadCharacteristics(mmcss); } catch { }
                if (rc != null) Marshal.ReleaseComObject(rc);
                if (ac != null) Marshal.ReleaseComObject(ac);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (fmtPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(fmtPtr);
                ev.Close();
            }
        }

        static void WriteSamples(IntPtr data, float[] src, int frames, Fmt f, int bps)
        {
            int frameBytes = f.Ch * bps;
            var buf = new byte[frames * frameBytes];
            int o = 0;
            for (int i = 0; i < frames; i++)
            {
                float v = src[i];
                if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                for (int c = 0; c < f.Ch; c++)
                {
                    if (f.Float)
                    {
                        var b = BitConverter.GetBytes(v);
                        buf[o] = b[0]; buf[o + 1] = b[1]; buf[o + 2] = b[2]; buf[o + 3] = b[3];
                    }
                    else if (bps == 2)
                    {
                        int s = (int)(v * 32767f);
                        buf[o] = (byte)s; buf[o + 1] = (byte)(s >> 8);
                    }
                    else if (bps == 3)
                    {
                        int s = (int)(v * 8388607f);
                        buf[o] = (byte)s; buf[o + 1] = (byte)(s >> 8); buf[o + 2] = (byte)(s >> 16);
                    }
                    else
                    {
                        int s = (int)(v * 2147483520f); // 24 veya 32 geçerli bit, üst bitler
                        buf[o] = (byte)s; buf[o + 1] = (byte)(s >> 8); buf[o + 2] = (byte)(s >> 16); buf[o + 3] = (byte)(s >> 24);
                    }
                    o += bps;
                }
            }
            Marshal.Copy(buf, 0, data, buf.Length);
        }
    }
}
