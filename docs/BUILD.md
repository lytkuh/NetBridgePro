# NetBridge Pro — Build Guide / راهنمای بیلد

<div dir="rtl">

## ⚡ فایل‌های آماده (بدون نیاز به IDE!)

اگر نمی‌خواهید هیچ چیزی بیلد کنید، این دو فایل را استفاده کنید:

| فایل | کجا | چه کار |
|---|---|---|
| `NetBridgePro-v1.0.apk` | کنار همین پوشه | روی گوشی نصب کنید (Install unknown sources) |
| `NetBridgePro-Windows-v1.0.zip` | کنار همین پوشه | Extract کنید → `NetBridge.Windows.exe` را اجرا کنید (بدون نصب .NET، بدون Visual Studio) |

سورس این پروژه در سندباکس واقعاً کامپایل شده است:
`assembleDebug` اندروید موفق، و `dotnet publish` ویندوز با **۰ خطا / ۰ هشدار**.

## پیش‌نیازها (محیط شما: هر دو IDE نصب است ✅)

| ابزار | نسخه | لینک |
|---|---|---|
| Android Studio | Ladybug (2024.2) یا جدیدتر | developer.android.com/studio |
| JDK | 17 (باندل‌شده در AS) | — |
| Visual Studio 2022 | 17.8+ | workload: **.NET desktop development** |
| .NET SDK | 8.0 | dotnet.microsoft.com/download/dotnet/8.0 |
| platform-tools (adb) | جدید | **از داخل خود برنامه** نصب می‌شود |

## الف) اپ اندروید

1. پوشه `android/` را در Android Studio باز کنید (File → Open).
2. صبر کنید Gradle sync تمام شود (اولین بار ~۵ دقیقه؛ Gradle 8.9 خودکار دانلود می‌شود).
3. گوشی را با USB وصل و USB debugging را در Developer options روشن کنید.
4. `Run ▶` — اپ روی گوشی نصب و اجرا می‌شود.

**CLI:** `cd android && ./gradlew assembleDebug` → خروجی:
`android/app/build/outputs/apk/debug/app-debug.apk`
(اگر gradlew wrapper jar نبود، از منوی Android Studio «Gradle wrapper» را fix کنید یا
`gradle wrapper` بزنید.)

### امضای Release
```bash
keytool -genkeypair -v -keystore netbridge.jks -keyalg RSA -keysize 2048 -validity 10000 -alias netbridge
# در app/build.gradle.kts بخش release: signingConfigs اضافه کنید
./gradlew assembleRelease
```

## ب) اپ ویندوز

### از Visual Studio
1. `windows/NetBridge.sln` را باز کنید.
2. Configuration = **Release**، پلتفرم Any CPU.
3. `Build → Build Solution` (Ctrl+Shift+B) → اجرا با F5.

### از CLI
```powershell
cd windows
dotnet build src -c Release -p:EnableWindowsTargeting=true
# اجرا:
src\bin\Release\net8.0-windows\NetBridge.Windows.exe
```

### انتشار تک‌فایلی
```powershell
dotnet publish src -c Release -r win-x64 --self-contained false `
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o publish
# خروجی: publish\NetBridge.Windows.exe (framework-dependent)
# self-contained (بدون نیاز به .NET نصب):
dotnet publish src -c Release -r win-x64 --self-contained true `
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o publish-sc
```
> روی لینوکس/مک هم می‌توانید فقط **کامپایل** کنید (`-p:EnableWindowsTargeting=true`)؛
> اجرا فقط روی ویندوز است. این پروژه با `dotnet build` لینوکسی تأیید شده: 0 خطا/۰ هشدار.

## ج) اولین راه‌اندازی (جفت‌سازی)

1. اپ ویندوز → **تنظیمات** → دکمه «نصب platform-tools (خودکار)».
2. گوشی: Developer options → USB debugging روشن.
3. اپ ویندوز → **تنظیمات** → «اجرا: فعال‌سازی و گرنت دسترسی‌ها»:
   - سرور adb اختصاصی روی 5038 بالا می‌آید،
   - روی گوشی پیام «Allow USB debugging؟» را با Always accept بپذیرید،
   - چهار دسترسی `pm grant` می‌شود،
   - wireless debugging خاموش می‌شود.
4. (اختیاری-امنیت بالا) Security mode را **Lockdown** بگذارید → `adb_enabled=0`.
5. دکمه **اتصال** در داشبورد. تمام — پروکسی سیستم خودکار تنظیم می‌شود.

## د) عیب‌یابی بیلد

| خطا | راه‌حل |
|---|---|
| `NETSDK1100` هنگام بیلد ویندوزی روی لینوکس | فلگ `-p:EnableWindowsTargeting=true` را بگذارید |
| Gradle: `kotlinCompilerExtensionVersion` mismatch | Kotlin 2.0.20 + compose compiler 1.5.15 با هم سازگارند؛ تغییر ندهید |
| VS: XAML designer خطا می‌دهد | فقط designer است؛ Build مشکلی ندارد |
| `adb: device unauthorized` | پیام روی گوشی را Accept کنید؛ بعد Revoke+جفت‌سازی مجدد |
| اجرای اپ پیام UAC می‌دهد | عمدی است (highestAvailable برای فایروال/`netsh`)؛ Cancel هم فقط فایروال را محدود می‌کند |

## هـ) مسیرهای داده در ویندوز

```
%ProgramData%\NetBridge\
├── settings.json          تنظیمات + توکن
├── keys\adbkey(.pub)      کلید اختصاصی adb
├── platform-tools\        platform-tools دانلودشده
├── firewall-rules.json    قواعد مسدودسازی برنامه‌ها
└── logs\app-YYYYMMDD.log
```

</div>
