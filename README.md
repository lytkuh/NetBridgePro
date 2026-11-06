# NetBridge Pro

<p align="center">
  <img src="art/logo/netbridge-logo-512.png" width="140" alt="NetBridge Pro logo" /><br/>
  <a href="https://github.com/lytkuh/NetBridgePro"><img alt="GitHub" src="https://img.shields.io/badge/GitHub-lytkuh%2FNetBridgePro-181717?logo=github" /></a>
  <a href="https://lytkuh.ir"><img alt="Website" src="https://img.shields.io/badge/Website-lytkuh.ir-22D3EE?logo=safari&logoColor=white" /></a>
  <img alt="License" src="https://img.shields.io/badge/License-MIT-34D399" />
</p>

<div dir="rtl">

**تترینگ حرفه‌ای گوشی ← ویندوز، در سطح PdaNet و بالاتر** — با کنترل کامل شبکه رادیویی (قفل 2G/3G/4G/5G)، سوییچر هوشمند سیگنال، و مدل امنیتی «دیباگ USB داخلی و غیرعمومی».

**دانلود / Downloads:** `NetBridgePro-Setup-v1.2.0.exe` (نصب‌کننده ویندوز) · `NetBridgePro-v1.2.0.apk` (اندروید) · ZIP پرتابل ویندوز — وضعیت سایر پلتفرم‌ها: `docs/PLATFORMS.md`

</div>

**Pro-grade phone-to-PC tethering** — SOCKS5/HTTP data plane over USB-NCM, ADB tunnel, Wi-Fi hotspot or Bluetooth PAN, plus full RAN mode control (2G/3G/4G/5G locks), a smart signal-based switcher, DoH DNS, per-app Windows firewall, and a privacy-first "private ADB" security model.

---

## ویژگی‌ها / Features

<div dir="rtl">

- **موتور داده پرسرعت**: پراکسی چندپروتکلی (SOCKS5 با UDP ASSOCIATE + HTTP CONNECT/absolute-form) روی یک درگاه، بافرهای ۲۵۶ کیلوبایتی، TCP_NODELAY، رله هم‌زمان دومسیره.
- **۴ روش اتصال** با تشخیص و fallback خودکار: USB تترینگ NCM/RNDIS (سریع‌ترین) → تونل ADB → هات‌اسپات Wi-Fi → بلوتوث PAN.
- **کنترل دکل و نسل**: قفل حالت شبکه بدون روت (نوشتن `preferred_network_mode` + چرخه هواپیما از طریق adb اختصاصی)، راستی‌آزمایی خودکار نتیجه، مقادیر چندکاندید برای فیرم‌ورهای مختلف.
- **سوییچر هوشمند**: سیاست‌های «حداکثر پایداری / حداکثر سرعت / باتری» با هیسترزیس RSRP/SINR، زمان خنک‌سازی و حداقل ماندگاری.
- **مانیتور سیگنال زنده**: RSRP/RSRQ/SINR/RSSI، باند، PCI، TAC، Cell ID، وضعیت NR (NSA/SA) هر ۱ ثانیه.
- **امنیت لایه‌ای**: توکن + HMAC-SHA256 برای پل و پراکسی، سرور adb اختصاصی روی درگاه 5038 با کلید مخصوص این PC، و «حالت قفل» که adb سراسری را خاموش می‌کند.
- **DNS امن**: حل نام روی گوشی (پیش‌فرض) یا DoH روی ویندوز (Cloudflare/Google/AdGuard) با کش TTL.
- **فایروال برنامه‌ای**: مسدودسازی اینترنت هر برنامه ویندوز از طریق Windows Firewall + قواعد ماندگار.
- **تست سرعت واقعی**: دانلود/آپلود/پینگ/جیتر از داخل مسیر کامل تونل (Cloudflare).
- **آمار و نمودار زنده**: سرعت لحظه‌ای، کل مصرف، اتصال‌های فعال، آمار سمت گوشی.
- **اتصال خودکار**: تلاش دوره‌ای با backoff، بازیابی بعد از قطعی USB، اجرای خودکار با ویندوز.
- **دوزبانه**: فارسی (RTL) + انگلیسی، تم دارک یکسان در دو پلتفرم.

</div>

English feature list mirrors the above; see `docs/ARCHITECTURE.md`.

## ساختار مخزن / Repository layout

```
NetBridgePro/
├── android/                    Kotlin + Compose (Gradle, minSdk 26, target 35)
│   └── app/src/main/java/pro/netbridge/android/
│       ├── core/               Prefs, Logger, TokenAuth (HMAC), AppState, Reflect
│       ├── proxy/              MultiProxyServer (SOCKS5/HTTP/UDP) + ProxyStats
│       ├── bridge/             BridgeServer (JSON lines + HMAC auth)
│       ├── net/                SignalMonitor, NetworkTypes (RIL modes), PhoneNet
│       ├── service/            ProxyService (foreground) + BootReceiver
│       └── ui/                 Compose UI (Dashboard/Network/Security/Settings)
├── windows/                    C# .NET 8 WPF (no external NuGet packages)
│   └── src/
│       ├── Core/               Constants, Settings, Logger, I18N, Mvvm, PairingWizard
│       ├── Adb/                AdbServerClient (server protocol), AdbServerManager, AdbKey
│       ├── Bridge/             BridgeClient (mirror of BridgeServer.kt)
│       ├── Net/                LocalProxy, DohClient, TetherMonitor, SystemProxySetter,
│       │                       ConnectionManager, NetModeController, SmartSwitcher, SpeedTest
│       ├── Firewall/           AppFirewall (INetFwPolicy2 per-app blocking)
│       ├── Theme/              Dark WPF theme (single dictionary)
│       └── Ui/                 MainWindow + 7 pages + SpeedChart control
├── docs/                       ARCHITECTURE, PROTOCOL, SECURITY, NETWORK-CONTROL, BUILD, ROADMAP
├── tools/                      helper scripts
└── LICENSE                     MIT
```

## شروع سریع / Quick start

<div dir="rtl">

۱. **اندروید**: پوشه `android` را در Android Studio باز کنید → `Run` → نصب روی گوشی.
۲. **ویندوز**: `windows/NetBridge.sln` را در Visual Studio 2022 (17.8+) باز کنید → `Start`.
   (یا CLI: `dotnet build windows/src -c Release -p:EnableWindowsTargeting=true`)
۳. در تنظیمات برنامه ویندوز، **adb اختصاصی** را نصب کنید (یک بار، ~۱۰ مگابایت).
۴. Developer options و USB debugging را روشن کنید، کابل را بزنید و دکمه
   **«اجرا: فعال‌سازی و گرنت دسترسی‌ها»** را بزنید؛ پیام مجوز روی گوشی را بپذیرید.
۵. توکن جفت‌سازی خودکار منتقل/تنظیم می‌شود؛ دکمه **اتصال** را بزنید. همین!

راهنمای کامل: `docs/BUILD.md` — امنیت: `docs/SECURITY.md` — قفل شبکه: `docs/NETWORK-CONTROL.md`

</div>

1. Open `android/` in Android Studio → Run on device.
2. Open `windows/NetBridge.sln` in VS 2022 → Start. (CLI: `dotnet build windows/src -c Release -p:EnableWindowsTargeting=true`)
3. In Settings install the bundled platform-tools (one click).
4. Enable USB debugging, plug the cable, press **"Run: activate & grant permissions"**, accept the prompt on the phone.
5. Press **Connect**. Full guides: `docs/BUILD.md`, security model: `docs/SECURITY.md`.

## درگاه‌ها / Ports

| Purpose | Port |
|---|---|
| Phone proxy (SOCKS5+HTTP multiprotocol, HMAC via RFC1929) | **59110** |
| PC local proxy for system apps (loopback only) | **59120** |
| Phone bridge (JSON lines + HMAC-SHA256 challenge) | **59123** |
| Private adb server (dedicated key) | **5038** |
| ADB forward endpoints (data / bridge) | 59121 / 59122 |

## وضعیت پایداری / Status

- Windows: **کامپایل‌شده و تست‌شده** — `dotnet build windows/src -c Release` (نسخه ۱.۲.۰ با آیکون و صفحه «درباره ما»).
- Android: نسخه ۱.۲.۰ — بیلد و امضای Release تأییدشده (`apksigner verify` پاس)، تست‌های واحد (JUnit) پاس.
- محدودیت‌های صادقانه (ناشی از اندروید، نه باگ): قفل باند واقعی به روت/OEM نیاز دارد (تجربی)، اعمال مود روی بعضی فیرم‌ورها چرخه هواپیما می‌خواهد، جدول `preferred_network_mode` بین سازندگان متفاوت است — برنامه با **راستی‌آزمایی چندکاندیدی** این را مدیریت می‌کند.

## درباره ما / About

<div dir="rtl">

**نت‌بریج پرو** یک راهکار کامل و حرفه‌ای برای اشتراک اینترنت از گوشی به کامپیوتر است؛ مهندسی‌شده برای سرعت، امنیت و کنترل کامل روی شبکه. این پروژه با عشق به فناوری و برای کاربران فارسی‌زبان ساخته شده است.

- **سازنده و توسعه‌دهنده:** محمدرضا خزاعی
- **وب‌سایت رسمی:** [lytkuh.ir](https://lytkuh.ir)
- **مخزن گیت‌هاب:** [github.com/lytkuh/NetBridgePro](https://github.com/lytkuh/NetBridgePro)
- **قدرت‌گرفته از:** ForgeAI-Studio
- **زیرمجموعه:** LytKuh Studio (استودیو لیت‌کوه)

</div>

**NetBridge Pro** is a complete, professional-grade tethering suite — engineered for speed, security and total control.

- **Developer:** Mohammadreza Khazaei (محمدرضا خزاعی)
- **Website:** [lytkuh.ir](https://lytkuh.ir)
- **GitHub:** [github.com/lytkuh/NetBridgePro](https://github.com/lytkuh/NetBridgePro)
- **Powered by:** ForgeAI-Studio
- **A subsidiary of:** LytKuh Studio

## عیب‌یابی بیلد / Build troubleshooting

<div dir="rtl">

- **خطای 404 از dl.google.com:** گوگل دانلودهای SDK/مِیون را برای برخی شبکه‌ها (از جمله ایران) مسدود می‌کند. `settings.gradle.kts` این پروژه به‌صورت پیش‌فرض از میرورهای عمومی Aliyun استفاده می‌کند و بعد از آن مخازن اصلی به‌عنوان fallback هستند؛ پس بیلد در هر شبکه‌ای کار می‌کند.
- **خطای «App not installed» هنگام نصب APK:** اگر قبلاً نسخه‌ای با کلید دیباگِ متفاوت نصب کرده‌اید، اندروید آپدیت را رد می‌کند (INSTALL_FAILED_UPDATE_INCOMPATIBLE). این پروژه از keystore ثابت `android/keystore/netbridge-release.jks` برای هر دو بیلد debug و release استفاده می‌کند؛ نسخه‌های قدیمی‌تر از قبل نصب‌شده را یک‌بار حذف کنید و بعد نسخه جدید را نصب کنید.
- Gradle wrapper این پروژه به `mirrors.cloud.tencent.com/gradle/` اشاره می‌کند تا دانلود خود Gradle هم بدون فیلترینگ کار کند.

</div>

## مجوز / License

MIT — see `LICENSE`.

