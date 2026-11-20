using System;
using System.Collections.Generic;

namespace SesKalkani
{
    /// <summary>Kardeşin (diğer bilgisayarın) mikrofon seviyesini veren kaynak.</summary>
    public interface IPartnerSource
    {
        /// <summary>Son windowMs içindeki en yüksek seviye (dB). Veri yoksa -200.</summary>
        float Peak(int windowMs);
    }

    /// <summary>
    /// Akıllı ses kapısı (noise gate).
    /// - Eşik altındaki sesleri keser.
    /// - Lookahead: sesi birkaç blok geciktirir, böylece kelimelerin başı kesilmez.
    /// - Hold: konuşma arasındaki kısa duraklamalarda kapı kapanmaz.
    /// - Kardeş modu: diğer bilgisayarın mikrofonu senden belirgin şekilde daha yüksek
    ///   duyuyorsa, konuşan kişi kardeşindir; senin mikrofonun susturulur.
    /// </summary>
    public class Gate
    {
        public const int Rate = 48000;
        public const int Block = 480; // 10 ms

        public volatile float ThresholdDb = -45f;
        public volatile float MarginDb = 8f;
        public volatile bool PartnerMode = true;

        public readonly int Lookahead = 4;   // 40 ms ileri bakış (Wi-Fi gecikmesine pay)
        public readonly int HoldBlocks = 25; // 250 ms tutma

        public IPartnerSource Partner;

        // Arayüz için son değerler
        public volatile float Level = -100f;
        public volatile float PartnerLevel = -200f;
        public volatile int State; // 0 = kapalı, 1 = açık, 2 = kardeş konuşuyor (susturuldu)

        readonly Queue<short[]> delay = new Queue<short[]>();
        readonly float[] hist = new float[16];
        int histPos;
        float hpX, hpY, gain;
        int hold;

        public Gate() { Reset(); }

        public void Reset()
        {
            delay.Clear();
            for (int i = 0; i < hist.Length; i++) hist[i] = -100f;
            histPos = 0; hpX = hpY = gain = 0; hold = 0;
            Level = -100f; PartnerLevel = -200f; State = 0;
        }

        /// <summary>Bloğun seviyesini dB olarak ölçer (100 Hz altı uğultu/fan sesi hariç).</summary>
        public float Measure(short[] b)
        {
            double s = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float x = b[i] / 32768f;
                float y = 0.987f * (hpY + x - hpX); // basit yüksek geçiren filtre
                hpX = x; hpY = y;
                s += y * y;
            }
            double rms = Math.Sqrt(s / Math.Max(1, b.Length));
            return (float)(20.0 * Math.Log10(rms + 1e-7));
        }

        public short[] Process(short[] input)
        {
            float lvl = Measure(input);
            Level = lvl;

            int n = Lookahead + 1;
            hist[histPos] = lvl;
            histPos = (histPos + 1) % n;

            delay.Enqueue((short[])input.Clone());
            if (delay.Count <= Lookahead) return new short[input.Length];
            short[] d = delay.Dequeue();

            // Geciktirilmiş bloktan şimdiye kadarki en yüksek kendi seviyem
            float myMax = -200f;
            for (int i = 0; i < n; i++) if (hist[i] > myMax) myMax = hist[i];

            float p = (PartnerMode && Partner != null) ? Partner.Peak(n * 10 + 60) : -200f;
            PartnerLevel = p;

            bool blocked = PartnerMode && p > -150f && p > ThresholdDb && (p - myMax) > MarginDb;
            bool want = myMax > ThresholdDb && !blocked;

            if (want) hold = HoldBlocks;
            else
            {
                if (blocked) hold = 0; // kardeş konuşmaya başladıysa hemen kapan
                if (hold > 0) hold--;
            }

            float target = hold > 0 ? 1f : 0f;
            State = target > 0 ? 1 : (blocked ? 2 : 0);

            short[] o = new short[d.Length];
            for (int i = 0; i < d.Length; i++)
            {
                // ~1 ms açılış, ~35 ms yumuşak kapanış; kardeş konuşuyorsa ~2 ms'de kapan
                float c = target > gain ? 0.02f : (blocked ? 0.01f : 0.0006f);
                gain += (target - gain) * c;
                if (gain < 1e-4f) gain = 0f;
                o[i] = (short)(d[i] * gain);
            }
            return o;
        }
    }
}
