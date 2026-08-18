# aRacnid GamepadApp

aRacnid GamepadApp, desteklenen fiziksel gamepad’leri tek bir giriş katmanında okuyup oyuna sanal **DualShock 4** veya **Xbox 360** kolu olarak sunan Windows uygulamasıdır. Profil, tuş/analog yön atama, deadzone, anti-deadzone, hassasiyet, titreşim gücü, DS4 lightbar ve sistem tepsisi desteği içerir.

## Desteklenen cihazlar

Fiziksel giriş:

- DualShock 4 v1/v2 — USB ve Bluetooth
- Sony DualShock 4 USB Wireless Adapter
- DualSense ve DualSense Edge
- Nintendo Switch Pro Controller
- Nintendo Joy-Con
- Logitech F310, F510 ve F710

Sanal çıkış:

- DualShock 4
- Xbox 360 Controller

Fiziksel Xbox ve Steam Controller girişi bu sürümün kapsamına dahil değildir. Uygulama yalnız yukarıdaki doğrulanmış aileleri kabul eder; kendi ViGEm sanal çıkışını fiziksel giriş olarak yeniden yakalamaz.

## Kurulum

1. GitHub Releases bölümündeki `aRacnid-GamepadApp-Setup-1.0.0-x64.exe` dosyasını çalıştırın.
2. Uygulamayı açın ve **Gelişmiş > Bileşenleri Yönet** bölümüne gidin.
3. Zorunlu **ViGEmBus** bileşenini yükleyin.
4. Çift input yaşamamak için önerilen **HidHide** bileşenini yükleyin ve **Yapılandır** düğmesini açın.
5. Fiziksel kolu bağlayın. İlk geçerli fiziksel rapor gelmeden sanal kol oluşturulmaz.

Setup ve portable paket **win-x64 self-contained** yayımlanır; ayrıca .NET Desktop Runtime kurmanız gerekmez. ViGEmBus sistem sürücüsü zorunludur. HidHide önerilir.

> Uygulama ve kurucu henüz ticari kod imzasıyla imzalanmadığı için Windows SmartScreen ilk çalıştırmada uyarı gösterebilir.

## HidHide yapılandırması

HidHide yalnızca bulunduğu bilgisayarda yapılandırılır; ayarı başka bilgisayara taşınmaz.

1. HidHide Applications sekmesine kurulu `GamepadApp.exe` yolunu ekleyin.
2. Devices sekmesinde fiziksel kolu seçin.
3. Cloaking’i etkinleştirin.
4. `joy.cpl` içinde oyuna yalnız sanal kolun göründüğünü doğrulayın.

Uygulamayı whitelist’e eklemeden fiziksel kolu gizlerseniz aRacnid fiziksel girişi okuyamaz.

## Kullanım notları

- Sanal kol yalnız fiziksel kol bağlı ve geçerli input üretirken bulunur.
- Profil değişiklikleri profil bazında saklanır; son kullanılan profil bir sonraki açılışta korunur.
- Tuş Atamaları penceresindeki değişiklikler **Kaydet** ile uygulanır. Kaydedilmemiş pencere kapatılırken uyarı gösterilir.
- `%100` hassasiyet fiziksel analog değerini değiştirmez. ViGEm’e her karede tek, tamamen hazırlanmış rapor gönderilir.
- Uygulama kapanırsa sanal kol ayrılır. Arka planda çalışması için **Gelişmiş > Sistem tepsisine küçült** seçeneğini açın.
- Profiller `%AppData%\GamepadApp\profiles.json` içinde tutulur. Atomik yazma ve `.bak` kurtarma kullanılır; kaldırıcı kullanıcı profillerini silmez.

## Sorun giderme

**Sanal kol görünmüyor**

- ViGEmBus’ın kurulu olduğunu **Gelişmiş > Bileşenleri Yönet** ekranından kontrol edin.
- Fiziksel kolu çıkarıp yeniden bağlayın.
- ViGEmBus güncellemesinden sonra yeniden başlatın; bazı yerinde güncellemelerde kurucuyu ikinci kez çalıştırmak gerekebilir.

**Oyun iki kol görüyor / menü iki kez ilerliyor**

- HidHide’ı yapılandırın.
- `GamepadApp.exe` dosyasının HidHide Applications listesinde olduğunu doğrulayın.
- Fiziksel kol gizli, sanal kol görünür olmalıdır.

**Portable paket açılmıyor**

- ZIP’i tamamen çıkarın; yalnız EXE’yi taşımayın.
- `SDL3.dll`, `HidSharp.dll`, `Nefarius.ViGEm.Client.dll`, `.deps.json` ve `.runtimeconfig.json` dosyaları EXE’nin yanında kalmalıdır.
- Yalnız x64 Windows desteklenir.

## Geliştirme

Gereksinimler:

- Windows 10/11 x64
- .NET 10 SDK
- ViGEmBus
- Setup üretmek için Inno Setup 6

```powershell
dotnet restore GamepadApp.csproj
dotnet build GamepadApp.csproj -c Release
dotnet build tests\GamepadApp.InputTests\GamepadApp.InputTests.csproj -c Release
tests\GamepadApp.InputTests\bin\Release\net10.0-windows\GamepadApp.InputTests.exe
```

Self-contained portable paket ve setup:

```powershell
.\scripts\build-release.ps1 -Version 1.0.0
```

Inno Setup kurulu değilse yalnız portable paketi üretmek için:

```powershell
.\scripts\build-release.ps1 -Version 1.0.0 -SkipInstaller
```

Çıktılar `artifacts` klasörüne yazılır.

## Test kapsamı

Otomatik testler DS4 USB/Bluetooth rapor ayrıştırma ve CRC’yi, sanal cihaz filtresini, SDL ABI’sini, Logitech/Nintendo/Sony allowlist’ini, trigger ve analog yön remap’ini, `%100` analog kimliğini, profil kurtarmayı ve tek-submit kaynak kuralını denetler. `--live` modu ViGEmBus üzerinde gerçek sanal DS4 ve Xbox hedefleri oluşturup PnP filtresini sınar.

Fiziksel DS4 akışı ayrıca DS4RawReader ile fiziksel ve sanal HID raporları karşılaştırılarak doğrulanmıştır. DualSense, Nintendo ve Logitech aileleri için bu bilgisayarda gerçek donanım bulunmadığından protokol/SDL fixture testleri yapılmıştır; her modelde fiziksel son kullanıcı testi yine önerilir.

Üçüncü taraf bileşen ve lisans bilgileri [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) dosyasındadır.

---

## English

aRacnid GamepadApp converts supported physical controllers into a virtual DualShock 4 or Xbox 360 controller on Windows. The self-contained x64 package does not require a separate .NET installation; ViGEmBus is required and HidHide is recommended to prevent double input. See the Turkish sections above for the complete device list, setup, HidHide configuration, troubleshooting, development commands, and verified test scope.
