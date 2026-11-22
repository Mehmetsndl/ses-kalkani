using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SesKalkani
{
    // ---------------------------------------------------------------- Windows ses API (winmm)
    static class Win
    {
        public const uint CALLBACK_NULL = 0, CALLBACK_EVENT = 0x50000;
        public const int WHDR_DONE = 1, WHDR_PREPARED = 2, WHDR_INQUEUE = 0x10;
        public const uint WAVE_MAPPER = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEINCAPS
        {
            public ushort wMid, wPid; public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats; public ushort wChannels, wReserved1;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEOUTCAPS
        {
            public ushort wMid, wPid; public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats; public ushort wChannels, wReserved1; public uint dwSupport;
        }

        public static readonly int HdrSize = Marshal.SizeOf(typeof(WAVEHDR));
        public static readonly int OffFlags = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");
        public static readonly int OffRec = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBytesRecorded");
        public static readonly int OffLen = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBufferLength");

        [DllImport("winmm.dll")] public static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll", EntryPoint = "waveInGetDevCapsW")] public static extern int waveInGetDevCaps(UIntPtr id, ref WAVEINCAPS c, uint size);
        [DllImport("winmm.dll")] public static extern int waveInOpen(out IntPtr h, UIntPtr id, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInStop(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInClose(IntPtr h);

        [DllImport("winmm.dll")] public static extern uint waveOutGetNumDevs();
        [DllImport("winmm.dll", EntryPoint = "waveOutGetDevCapsW")] public static extern int waveOutGetDevCaps(UIntPtr id, ref WAVEOUTCAPS c, uint size);
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr h, UIntPtr id, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr h);

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    }

    public class DevItem
    {
        public uint Id; public string Name;
        public override string ToString() { return Name; }
    }

    public static class Devices
    {
        public static List<DevItem> Inputs()
        {
            var l = new List<DevItem>();
#if PREVIEW
            l.Add(new DevItem { Id = Win.WAVE_MAPPER, Name = "Windows varsayılan mikrofonu" });
            l.Add(new DevItem { Id = 0, Name = "Mikrofon (ATK N9 Ultra)" });
            return l;
#endif
            l.Add(new DevItem { Id = Win.WAVE_MAPPER, Name = "Windows varsayılan mikrofonu" });
            uint n = Win.waveInGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var c = new Win.WAVEINCAPS();
                if (Win.waveInGetDevCaps(new UIntPtr(i), ref c, (uint)Marshal.SizeOf(c)) == 0)
                    l.Add(new DevItem { Id = i, Name = c.szPname });
            }
            return l;
        }

        public static List<DevItem> Outputs()
        {
            var l = new List<DevItem>();
#if PREVIEW
            l.Add(new DevItem { Id = 0, Name = "Hoparlör (ATK N9 Ultra)" });
            l.Add(new DevItem { Id = 1, Name = "CABLE Input (VB-Audio Virtual C" });
            return l;
#endif
            uint n = Win.waveOutGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var c = new Win.WAVEOUTCAPS();
                if (Win.waveOutGetDevCaps(new UIntPtr(i), ref c, (uint)Marshal.SizeOf(c)) == 0)
                    l.Add(new DevItem { Id = i, Name = c.szPname });
            }
            return l;
        }
    }

    // ---------------------------------------------------------------- Yönetilmeyen bellekte ses tamponu
    class Buf
    {
        public IntPtr Hdr, Data; public int Bytes;
        public static Buf Create(int bytes)
        {
            var b = new Buf { Bytes = bytes, Data = Marshal.AllocHGlobal(bytes), Hdr = Marshal.AllocHGlobal(Win.HdrSize) };
            var h = new Win.WAVEHDR { lpData = b.Data, dwBufferLength = (uint)bytes };
            Marshal.StructureToPtr(h, b.Hdr, false);
            return b;
        }
        public int Flags { get { return Marshal.ReadInt32(Hdr, Win.OffFlags); } set { Marshal.WriteInt32(Hdr, Win.OffFlags, value); } }
        public void Free() { Marshal.FreeHGlobal(Hdr); Marshal.FreeHGlobal(Data); }
    }

    // ---------------------------------------------------------------- Kardeş bağlantısı (yerel ağ, UDP)
    public class PartnerLink : IPartnerSource
    {
        public const int Port = 47811;
        readonly string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        readonly string myName = Environment.MachineName;
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly object lk = new object();
        readonly List<KeyValuePair<long, float>> pk = new List<KeyValuePair<long, float>>();
        UdpClient rx, tx; Thread th; volatile bool run;
        long lastSeen = -100000;
        List<IPEndPoint> targets = new List<IPEndPoint>(); long targetsAt = -100000;
        public volatile string PartnerName = "";
        public volatile string Error;

        public bool Connected { get { return sw.ElapsedMilliseconds - Interlocked.Read(ref lastSeen) < 2000; } }

        public void Start()
        {
            if (run) return;
            try
            {
                rx = new UdpClient();
                rx.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                rx.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                tx = new UdpClient(); tx.EnableBroadcast = true;
            }
            catch (Exception e) { Error = "Ağ başlatılamadı: " + e.Message; return; }
            run = true;
            th = new Thread(Loop) { IsBackground = true, Name = "KardesAg" };
            th.Start();
        }

        public void Stop()
        {
            run = false;
            try { if (rx != null) rx.Close(); } catch { }
            try { if (tx != null) tx.Close(); } catch { }
            rx = tx = null;
        }

        void Loop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (run)
            {
                try
                {
                    byte[] b = rx.Receive(ref ep);
                    var p = Encoding.UTF8.GetString(b).Split(';');
                    if (p.Length < 4 || p[0] != "SK1" || p[1] == id) continue;
                    float l = float.Parse(p[2], CultureInfo.InvariantCulture);
                    long now = sw.ElapsedMilliseconds;
                    lock (lk)
                    {
                        pk.Add(new KeyValuePair<long, float>(now, l));
                        if (pk.Count > 600) pk.RemoveRange(0, 300);
                    }
                    Interlocked.Exchange(ref lastSeen, now);
                    PartnerName = p[3];
                }
                catch { if (!run) break; Thread.Sleep(5); }
            }
        }

        public float Peak(int windowMs)
        {
            long now = sw.ElapsedMilliseconds; float m = -200f;
            lock (lk)
            {
                for (int i = pk.Count - 1; i >= 0; i--)
                {
                    if (now - pk[i].Key > windowMs) break;
                    if (pk[i].Value > m) m = pk[i].Value;
                }
            }
            return m;
        }

        public void Send(float level)
        {
            var t = tx; if (!run || t == null) return;
            long now = sw.ElapsedMilliseconds;
            if (now - targetsAt > 5000) { targets = BuildTargets(); targetsAt = now; }
            byte[] b = Encoding.UTF8.GetBytes("SK1;" + id + ";" + level.ToString("F1", CultureInfo.InvariantCulture) + ";" + myName);
            foreach (var ep in targets) { try { t.Send(b, b.Length, ep); } catch { } }
        }

        static List<IPEndPoint> BuildTargets()
        {
            var l = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, Port) };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] a = ua.Address.GetAddressBytes(), m = ua.IPv4Mask.GetAddressBytes();
                        if (m[0] == 0) continue;
                        var bc = new byte[4];
                        for (int i = 0; i < 4; i++) bc[i] = (byte)(a[i] | ~m[i]);
                        l.Add(new IPEndPoint(new IPAddress(bc), Port));
                    }
                }
            }
            catch { }
            return l;
        }
    }

    // ---------------------------------------------------------------- Ses motoru: mikrofon -> kapı -> sanal mikrofon
    public class AudioEngine
    {
        const int InBufs = 8, OutBufs = 16, MaxQueued = 10;
        public readonly Gate Gate = new Gate();
        public readonly PartnerLink Link = new PartnerLink();
        IntPtr hIn, hOut; Buf[] inB, outB; AutoResetEvent ev; Thread th;
        volatile bool run;
        public volatile string Error;
        public bool Running { get { return run; } }

        // Yayın koruması: sanal kabloya özel modda yaz (Discord yayınına mikrofon karışmasın)
        WasapiExclusiveOut ex;
        public bool Protected { get { return ex != null; } }
        public string ProtectInfo = "";

        // Ölçüm (otomatik ayar)
        readonly object calLk = new object();
        List<float> cal; int calLeft;
        public bool Calibrating { get { lock (calLk) return calLeft > 0; } }
        public void StartCalibration(int ms) { lock (calLk) { cal = new List<float>(); calLeft = ms / 10; } }
        public List<float> TakeCalibration() { lock (calLk) { var c = cal; cal = null; calLeft = 0; return c; } }

        public AudioEngine() { Gate.Partner = Link; }

        public string Start(uint inDev, uint outDev, string outName, bool protect)
        {
            if (run) return null;
            ex = null; ProtectInfo = "";
            if (protect)
            {
                var w = new WasapiExclusiveOut();
                string we = w.Start(outName);
                if (we == null) { ex = w; ProtectInfo = w.FormatInfo; }
                else ProtectInfo = we;
            }
            var f = new Win.WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Gate.Rate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = Gate.Rate * 2 };
            ev = new AutoResetEvent(false);
            int r = Win.waveInOpen(out hIn, new UIntPtr(inDev), ref f, ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, Win.CALLBACK_EVENT);
            if (r != 0) { hIn = IntPtr.Zero; if (ex != null) { ex.Stop(); ex = null; } return "Mikrofon açılamadı (hata " + r + "). Başka bir uygulama mikrofonu özel kullanımda olabilir."; }
            if (r != 0 && ex != null) { ex.Stop(); ex = null; }
            if (ex == null)
            {
                r = Win.waveOutOpen(out hOut, new UIntPtr(outDev), ref f, IntPtr.Zero, IntPtr.Zero, Win.CALLBACK_NULL);
                if (r != 0) { Win.waveInClose(hIn); hIn = hOut = IntPtr.Zero; return "Sanal mikrofon açılamadı (hata " + r + ")."; }
            }

            int bytes = Gate.Block * 2;
            inB = new Buf[InBufs];
            for (int i = 0; i < InBufs; i++)
            {
                inB[i] = Buf.Create(bytes);
                Win.waveInPrepareHeader(hIn, inB[i].Hdr, (uint)Win.HdrSize);
                Win.waveInAddBuffer(hIn, inB[i].Hdr, (uint)Win.HdrSize);
            }
            outB = new Buf[ex == null ? OutBufs : 0];
            for (int i = 0; i < outB.Length; i++)
            {
                outB[i] = Buf.Create(bytes);
                Win.waveOutPrepareHeader(hOut, outB[i].Hdr, (uint)Win.HdrSize);
            }
            Gate.Reset();
            run = true;
            Win.waveInStart(hIn);
            th = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "Ses" };
            th.Start();
            return null;
        }

        public void Stop()
        {
            if (!run) return;
            run = false;
            ev.Set();
            th.Join(1000);
            Win.waveInStop(hIn); Win.waveInReset(hIn);
            foreach (var b in inB) Win.waveInUnprepareHeader(hIn, b.Hdr, (uint)Win.HdrSize);
            Win.waveInClose(hIn);
            if (ex != null) { ex.Stop(); ex = null; }
            else
            {
                Win.waveOutReset(hOut);
                foreach (var b in outB) Win.waveOutUnprepareHeader(hOut, b.Hdr, (uint)Win.HdrSize);
                Win.waveOutClose(hOut);
            }
            foreach (var b in inB) b.Free();
            foreach (var b in outB) b.Free();
            ev.Close();
            hIn = hOut = IntPtr.Zero;
            Gate.Reset();
        }

        short[] acc = new short[Gate.Block * 4]; int accN;

        void Loop()
        {
            try
            {
                WriteOut(new short[Gate.Block]); WriteOut(new short[Gate.Block]); // küçük tampon
                int next = 0;
                while (run)
                {
                    ev.WaitOne(50);
                    if (ex != null && ex.Error != null) { Error = ex.Error; ex.Error = null; }
                    while (run)
                    {
                        var b = inB[next];
                        int fl = b.Flags;
                        if ((fl & Win.WHDR_DONE) == 0) break;
                        int n = Marshal.ReadInt32(b.Hdr, Win.OffRec) / 2;
                        if (n > 0)
                        {
                            if (accN + n > acc.Length) accN = 0;
                            Marshal.Copy(b.Data, acc, accN, n);
                            accN += n;
                            while (accN >= Gate.Block)
                            {
                                var chunk = new short[Gate.Block];
                                Array.Copy(acc, chunk, Gate.Block);
                                Array.Copy(acc, Gate.Block, acc, 0, accN - Gate.Block);
                                accN -= Gate.Block;
                                Handle(chunk);
                            }
                        }
                        b.Flags = fl & ~Win.WHDR_DONE;
                        Marshal.WriteInt32(b.Hdr, Win.OffRec, 0);
                        Win.waveInAddBuffer(hIn, b.Hdr, (uint)Win.HdrSize);
                        next = (next + 1) % inB.Length;
                    }
                }
            }
            catch (Exception e) { Error = "Ses hatası: " + e.Message; }
        }

        void Handle(short[] chunk)
        {
            var o = Gate.Process(chunk);
            float lvl = Gate.Level;
            if (Gate.PartnerMode) Link.Send(lvl);
            lock (calLk)
            {
                if (calLeft > 0 && cal != null) { cal.Add(lvl); calLeft--; }
            }
            WriteOut(o);
        }

        void WriteOut(short[] o)
        {
            if (ex != null) { if (ex.Fifo != null) ex.Fifo.Write(o); return; }
            int queued = 0; Buf free = null;
            foreach (var b in outB)
            {
                if ((b.Flags & Win.WHDR_INQUEUE) != 0) queued++;
                else if (free == null) free = b;
            }
            if (queued >= MaxQueued || free == null) return; // saat kayması: blok atla
            if (queued == 0 && o.Length > 0)
            {
                // tampon boşaldı: araya küçük bir sessizlik koy
                Put(free, new short[Gate.Block]);
                free = null;
                foreach (var b in outB) if ((b.Flags & Win.WHDR_INQUEUE) == 0) { free = b; break; }
                if (free == null) return;
            }
            Put(free, o);
        }

        void Put(Buf b, short[] o)
        {
            Marshal.Copy(o, 0, b.Data, o.Length);
            Marshal.WriteInt32(b.Hdr, Win.OffLen, o.Length * 2);
            b.Flags = b.Flags & ~Win.WHDR_DONE;
            Win.waveOutWrite(hOut, b.Hdr, (uint)Win.HdrSize);
        }
    }
}
