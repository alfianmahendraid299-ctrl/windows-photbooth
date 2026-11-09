

<img width="1696" height="2528" alt="Gemini_Generated_Image_1uwdq71uwdq71uwd" src="https://github.com/user-attachments/assets/dcfe9bbc-e337-46a9-9e88-41b15de45123" />

# Photo Booth untuk Windows (Terinsipirasi oleh MacOS PhotoBooth)

Aplikasi WPF native (.NET 8) + OpenCvSharp.

# Update V1.0.2 
New Facemoji 3D 
<img width="1280" height="720" alt="Photo_20261109_090907" src="https://github.com/user-attachments/assets/0e2df7d1-5dc1-4866-a4b9-da066136862a" />
<img width="1280" height="720" alt="Photo_20261109_091027" src="https://github.com/user-attachments/assets/6af5b248-cb81-4241-b54a-36ba7638b5ce" />

## Cara menjalankan

1. Install **.NET 8 SDK** (https://dotnet.microsoft.com/download).
2. Buka terminal di folder ini:

   ```
   dotnet run -c Release
   ```

3. Untuk membuat file .exe yang bisa dibagikan:

   ```
   dotnet publish -c Release -r win-x64 --self-contained true -o publish
   ```

   Hasilnya ada di `bin\Release\net8.0-windows\win-x64\publish\`.
   (Atau buka `PhotoBooth.csproj` di Visual Studio 2022 lalu tekan F5.)

## Fitur

- **Pengaturan (ikon gerigi di dock):** watermark "Shot on" dengan teks sendiri, cap waktu di atas watermark, grid 3×3 / 4×4, suara jepretan, suara timer,
  auto filter AI per scene, palm shoot, cheese shoot, rekam suara untuk video, dan pilihan folder hasil.
- **Timer foto:** Tanpa timer (langsung jepret) / 3 / 5 / 10 detik, hanya untuk mode Foto. Ada bip tiap detik dan suara jepretan (bisa dimatikan).
- **Video dengan suara + jeda:** merekam mikrofon bersama video, tombol jeda/lanjut muncul di atas dock saat merekam. Hasil akhirnya MP4 (H.264 + AAC) lewat Media Foundation bawaan Windows; jika konversi gagal, berkas AVI tetap tersimpan.
- **Auto filter AI (scene):** MobileNetV2 di NPU/GPU/CPU mengenali makanan, pemandangan, langit, malam, potret, dan hewan, lalu menata warna otomatis (hanya saat efek Normal). Label scene tampil di kiri atas.
- **Palm shoot (eksperimental):** telapak terbuka menghadap kamera ±1 detik memicu foto dengan hitung mundur 3 detik. **Cheese shoot:** ucapkan "cheese" (butuh mikrofon, pengenalan suara Windows, dan paket bahasa English US).
- **Penyesuaian cahaya:** tombol matahari di dock membuka slider Kecerahan, Kontras, dan Eksposur (berlaku untuk pratinjau, foto, dan video, tersimpan otomatis).
  Tombol "Otomatis" meredam gambar yang terlalu terang secara halus (tidak pernah membuat gambar lebih terang).
- **Kamera aman:** mode kamera yang berhasil (resolusi, format, backend) disimpan, jadi peluncuran berikutnya membuka kamera satu kali saja.
  Resolusi tidak pernah diubah pada kamera yang sedang menyala. Jika peluncuran sebelumnya crash saat membuka kamera, aplikasi otomatis memakai "mode aman" (720p).
  Kesalahan dicatat di `%LOCALAPPDATA%\PhotoBooth\crash.log`.
- **Resolusi & fps kamera otomatis maksimal:** saat kamera dinyalakan, aplikasi menegosiasikan mode terbaik (format MJPG, 1080p → 720p → 540p → 480p, backend DirectShow lalu Media Foundation) dan mengukur fps nyata.
  Hasilnya muncul sebagai notifikasi (contoh: 1280×720 · 30 fps · MJPG). Bisa dipaksa lewat tombol Kamera → Resolusi kamera.
- **Galeri gaya Dynamic Island:** arahkan kursor ke tombol Galeri (ikon foto terakhir) di dock, pulau berisi thumbnail muncul dengan efek pegas dan fade.
  Pulau hilang sendiri 5 detik setelah kursor tidak di tombol/pulau. Klik tombol Galeri atau pulau untuk menyematkannya (tidak hilang sendiri);
  tombol X di pulau (atau Esc) menyembunyikannya. Setelah foto/video baru, pulau muncul sebentar otomatis.
- Klik thumbnail untuk pratinjau besar; tombol X di kanan atas (atau Esc) menutupnya dengan fade out.
- **Timer foto:** tombol Timer di dock memilih 3 / 5 / 10 detik. Pilihan hanya berlaku di mode **Foto**; mode 4 Foto tetap 3 detik per foto.
- **72 efek dalam 8 halaman** (geser dengan tombol panah atau tombol ← → di panel efek): 27 filter (termasuk Fish Eye, Pusaran, Riak Air, Matrix, VHS, Halftone), serta 45 efek AR (stiker, warp & makeup, Hulk marah, mata laser, api, petir, HUD Terminator, dan lainnya)
- **Stiker AR 3D & bergerak:** stiker mengikuti arah kepala (menoleh, mengangguk, miring) dan sebagian beranimasi (api, kelelawar & kupu-kupu yang mengorbit, hujan hati, bintang pusing)
- **Efek AR** memakai pelacak wajah 478 titik (MediaPipe Face Mesh) yang dijalankan lewat **Windows ML**:
  otomatis memilih **NPU** bila ada (Intel / Qualcomm / AMD, butuh Windows 11 24H2+), lalu **GPU** (DirectML), lalu CPU.
  Pilihan bisa diganti lewat chip "AI" di kanan atas.
- Tampilan bergaya liquid glass / macOS Tahoe: pratinjau memenuhi jendela, kontrol melayang di dock kaca
- Jendela langsung **maximize** saat dibuka, tanpa bingkai Windows; tombol merah / kuning / hijau (tutup / minimize / maximize-restore) ada di kiri atas seperti Mac
- Pemilih mode berbentuk **slider** (4 Foto / Foto / Video): klik atau geser, thumb meluncur dengan animasi
- Animasi: hitung mundur membesar-memudar, panel efek muncul dengan efek pegas, tombol shutter berubah jadi kotak saat merekam, notifikasi nama efek / kamera
- Pratinjau kamera langsung, otomatis di-mirror seperti Photo Booth
- Mode **4 Foto** (grid 2x2), **Foto** tunggal, dan **Video**
- Hitung mundur 3-2-1 untuk **setiap** foto (di mode 4 Foto tiap foto punya timer sendiri), kilatan layar, dan suara
- Tombol **Kamera** untuk memilih / mengganti webcam, plus "Segarkan daftar kamera" kalau webcam baru dicolok
- 9 efek dengan pratinjau langsung 3x3: Normal, Hitam Putih, Sepia, Komik,
  Termal, Sinar-X, Pop Art, Pensil, Lukisan
- Strip thumbnail di bawah, klik untuk melihat, klik kanan untuk Salin / Tampilkan di folder / Hapus
- Hasil tersimpan di `Pictures\Photo Booth`

## Pintasan

| Tombol | Fungsi |
|---|---|
| Spasi / Enter | Ambil foto / mulai-stop rekam |
| Esc | Batalkan hitung mundur / tutup panel efek / kembali ke kamera |
| Delete | Hapus item terpilih |

## Catatan

- Nama webcam tidak bisa dibaca langsung oleh OpenCV, jadi daftar kamera ditampilkan sebagai "Kamera 1, 2, ..." beserta resolusinya.
- Efek "kaca" disimulasikan dengan lapisan transparan, kilau, dan garis tepi. Blur sungguhan dari video di belakang panel tidak dipakai karena berat untuk video langsung di WPF.
- Ikon aplikasi ada di `app.ico`; ganti file itu (format .ico) kalau mau ikon lain.
- Video direkam tanpa suara (OpenCV tidak merekam audio).
- Efek "Lukisan" paling berat; kalau kamera terasa patah-patah, pilih efek lain.
- Kalau nama user Windows memuat karakter non-ASCII, perekaman video bisa gagal
  karena keterbatasan OpenCV pada path Unicode.

## Efek AR: cara kerja & persiapan

- Pertama kali memilih efek AR, aplikasi meminta izin lalu mengunduh: model pelacak wajah (±5 MB, diverifikasi hash SHA-256),
  model deteksi YuNet (±0,2 MB), dan akselerator AI dari Microsoft (Windows ML) bila perangkat mendukung.
  Unduhan disimpan di `%LOCALAPPDATA%\PhotoBooth\models`.
- **Offline / installer:** taruh `face_landmarks.onnx` dan `face_detection_yunet_2023mar.onnx` di folder `Assets\Models`
  sebelum publish, maka aplikasi memakainya langsung tanpa mengunduh.
- Model pertama kali di NPU bisa butuh waktu kompilasi lebih lama (detik sampai menit); setelah itu di-cache.
- Satu wajah dilacak pada satu waktu (wajah terbesar).
- Mode "Potret" hanya perkiraan (elips kepala-bahu), bukan segmentasi orang sungguhan.

## Menambah / mengubah stiker AR

1. Letakkan PNG transparan di `Assets\Stickers`, atau edit `tools/make_stickers.py` lalu jalankan `python tools/make_stickers.py`
   (membuat ulang semua stiker dan `ArCatalog.cs`; tambahkan `--mock` untuk pratinjau penempatan pada wajah tiruan).
2. Atur posisi di `ArCatalog.cs`: jangkar (kepala, mata, hidung, mulut...), lebar relatif lebar wajah, titik pivot, dan offset.
3. Daftarkan nama efek baru di `EffectLib.cs` (enum `EffectKind` dan tabel `All`).
