Aynı odada oynarken kardeşinin sesi senin mikrofonundan Discord'a gitmesin diye küçük bir Windows programı.

### Bu sürümde yenilikler (1.1.0)
- **Yayın koruması:** Discord'da ekranı sesli paylaşınca senin sesin yayına da karışıp eko yapıyordu. Ses Kalkanı artık
  sanal mikrofona Windows ses karıştırıcısını atlayan özel mod ile yazıyor; Discord yayını mikrofonunu göremiyor.
  BAŞLAT'tan sonra cihaz kartında yeşil **✓ Yayın koruması açık** yazmalı. Turuncu yazıyorsa
  [Sorun giderme](https://github.com/Mehmetsndl/ses-kalkani#sorun-giderme) bölümüne bak.

### İndir
**SesKalkani.exe**: kurulum gerektirmez, çift tıkla çalışır. Discord'a ses vermek için ücretsiz [VB-Cable](https://vb-audio.com/Cable/) gerekir. Adım adım kurulum için [README](https://github.com/Mehmetsndl/ses-kalkani#kurulum).

Eski sürümü kullanıyorsan: simge alanından **sağ tık → Çıkış**, yeni exe'yi eskisinin yerine koy ve bir kere çift tıklayarak aç.

İmzasız bir program olduğu için Windows SmartScreen uyarı verebilir: **Ek bilgi → Yine de çalıştır**.

---

**English:** A tiny Windows noise gate for two people gaming in the same room. Requires the free VB-Cable virtual audio driver.
New in 1.1.0: stream protection — output to the virtual cable uses WASAPI exclusive mode, so Discord's screen-share audio capture no longer picks up your mic (no more echo).
