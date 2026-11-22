using System;

namespace SesKalkani
{
    /// <summary>
    /// 48 kHz mono bloklarını alır, cihaz örnekleme hızına çevirir ve tamponlar.
    /// Mikrofon ile sanal kablonun saatleri hafif farklı işler: tampon çok dolarsa fazlası atılır,
    /// boşalırsa sessizlik çalınır ve tampon tekrar dolana kadar beklenir.
    /// </summary>
    public class SampleFifo
    {
        readonly object lk = new object();
        readonly float[] ring;
        int head, count;
        readonly double step;  // her çıkış örneği için giriş örneği adımı
        double pos;            // son girişe göre kesirli konum
        float prev;            // bir önceki bloğun son örneği
        readonly int target, maxFill;
        bool priming = true;
        public int Underruns, Drops;

        public SampleFifo(int deviceRate, int targetMs)
        {
            ring = new float[deviceRate]; // 1 saniye
            step = Gate.Rate / (double)deviceRate;
            target = deviceRate * targetMs / 1000;
            maxFill = target * 3 + deviceRate / 50; // hedefin 3 katı + 20 ms
            pos = 0;
        }

        public int Count { get { lock (lk) return count; } }

        public void Write(short[] input)
        {
            lock (lk)
            {
                // Doğrusal ara değerleme ile hız dönüşümü (48000 → cihaz hızı)
                int n = input.Length;
                while (pos < n)
                {
                    int i = (int)Math.Floor(pos);
                    double fr = pos - i;
                    float a = i - 1 < 0 ? prev : input[i - 1] / 32768f; // pos, i-1 ile i arasını gösterir
                    float b = input[i] / 32768f;
                    Push((float)(a + (b - a) * fr));
                    pos += step;
                }
                pos -= n;
                prev = input[n - 1] / 32768f;

                if (count > maxFill) { int drop = count - target; head = (head + drop) % ring.Length; count -= drop; Drops++; }
            }
        }

        void Push(float v)
        {
            if (count == ring.Length) { head = (head + 1) % ring.Length; count--; }
            ring[(head + count) % ring.Length] = v;
            count++;
        }

        /// <summary>frames kadar örnek okur; yeterli yoksa sessizlikle tamamlar.</summary>
        public void Read(float[] dst, int frames)
        {
            lock (lk)
            {
                if (priming && count >= target) priming = false;
                int take = priming ? 0 : Math.Min(frames, count);
                for (int i = 0; i < take; i++) dst[i] = ring[(head + i) % ring.Length];
                head = (head + take) % ring.Length; count -= take;
                for (int i = take; i < frames; i++) dst[i] = 0f;
                if (!priming && take < frames) { priming = true; Underruns++; }
            }
        }
    }
}
