# پشتیبانی پلتفرم‌ها / Platform Support — NetBridge Pro v1.2.0

<div dir="rtl">

## وضعیت فعلی

| پلتفرم | وضعیت | فایل نصبی |
|---|---|---|
| **ویندوز ۱۰/۱۱ (x64)** | ✅ کامل و تست‌شده | `NetBridgePro-Setup-v1.2.0.exe` یا ZIP پرتابل |
| **اندروید ۸ به بالا** | ✅ کامل و تست‌شده | `NetBridgePro-v1.2.0.apk` |
| **لینوکس** | ⚠️ هنوز پورت نشده | — |
| **مک (macOS)** | ⚠️ هنوز پورت نشده | — |
| **آیفون (iOS)** | ⚠️ هنوز پورت نشده | — |

## چرا لینوکس و مک هنوز نسخه ندارند؟

رابط کاربری نسخه ویندوز با **WPF** ساخته شده که فناوری آن **فقط روی ویندوز اجرا می‌شود**
(میکروسافت WPF را برای لینوکس/مک منتشر نکرده است). هستهٔ برنامه (پراکسی SOCKS5/HTTP،
پل کنترل، کلاینت adb، DNS امن) کد دات‌نت استاندارد است و قابل انتقال است، ولی لایهٔ
نمایش باید از نو نوشته شود.

**مسیر پیشنهادی پورت:** فریم‌ورک **Avalonia UI** (رابط کاربری دات‌نت که روی ویندوز،
لینوکس و مک اجرا می‌شود) — منطق برنامه تقریباً بدون تغییر می‌ماند و فقط Viewها بازنویسی
می‌شوند. بسته‌های انتشار هم برای لینوکس `AppImage`/`deb` و برای مک `dmg` خواهند بود.

## چرا آیفون هنوز نسخه ندارد؟

دو محدودیت مستقل وجود دارد:

1. **کد اپ فعلی اندروید** با Kotlin/Jetpack Compose نوشته شده که فقط اندروید است.
   برای آیفون باید نسخه iOS جداگانه ساخته شود (پیشنهاد: **Compose Multiplatform**
   که بخش بزرگی از کد اندروید را دوباره استفاده می‌کند، یا MAUI).
2. **ساخت و نصب اپ iOS بدون اپل ممکن نیست:** برای کامپایل نهایی، امضا (codesign)
   و نصب روی آیفون به **مک با Xcode** و حساب **Apple Developer** (سالانه ۹۹ دلار،
   یا نصب آزمایشی ۷ روزه با حساب رایگان) نیاز است. هیچ ابزاری روی ویندوز نمی‌تواند
   فایل نصبی آیفون امضا‌شده تولید کند — این محدودیت اپل است، نه پروژه.

## نقشه راه پیشنهادی

- [ ] v1.3 — پورت رابط کاربری به Avalonia (ویندوز + لینوکس + مک با یک کد)
- [ ] v1.4 — بسته‌های Linux (AppImage/deb/rpm) و macOS (dmg، امضای notarization)
- [ ] v2.0 — نسخه موبایل چندپلتفرمی (Compose Multiplatform) برای اندروید + iOS

سؤال یا اولویت‌بندی؟ — سازنده: **محمدرضا خزاعی** — [lytkuh.ir](https://lytkuh.ir) —
[github.com/lytkuh/NetBridgePro](https://github.com/lytkuh/NetBridgePro)

</div>

---

## Platform support (English)

| Platform | Status | Installer |
|---|---|---|
| **Windows 10/11 (x64)** | ✅ Fully supported | `NetBridgePro-Setup-v1.2.0.exe` or portable ZIP |
| **Android 8+** | ✅ Fully supported | `NetBridgePro-v1.2.0.apk` |
| **Linux** | ⚠️ Not ported yet | — |
| **macOS** | ⚠️ Not ported yet | — |
| **iOS** | ⚠️ Not ported yet | — |

**Why:** the Windows UI uses **WPF**, which is Windows-only by design. The core
(proxy, bridge, adb client, DoH) is standard .NET and portable — the UI layer needs
rewriting (recommended: **Avalonia UI** for desktop, **Compose Multiplatform** or
MAUI for mobile). iOS additionally requires **Xcode on macOS** and an **Apple
Developer account** for signing; no Windows toolchain can produce a signed iOS build.
