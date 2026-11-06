# NetBridge Pro — Roadmap

<div dir="rtl">

## v1.0 (همین نسخه)
- چهار ترنسپورت + تشخیص خودکار، پراکسی چندپروتکلی، پل HMAC،
  قفل مود چندکاندیدی + چرخه هواپیما، سوییچر هوشمند، مانیتور سیگنال کامل،
  DoH + کش، فایروال برنامه‌ای، اسپیدتست درون‌تونلی، حالت Lockdown، دوزبانه RTL.

## v1.1 — قفل باند واقعی‌تر
- **Band Steering**: چرخه ثبت‌مجدد خودکار وقتی باند فعال خارج از لیست مجاز کاربر است
  (بدون روت، با محدودیت نرخ چرخه).
- پروفایل‌ساز OEM برای جدول `preferred_network_mode` (سامسونگ/شیائومی/پیکسل/آنر) قابل ویرایش از UI.
- TLS اختیاری سرتاسری روی پل (حالت Wi-Fi عمومی).

## v1.2 — دیتاپلین سطح‌بالا
- **TUN کامل با Wintun**: رهگیری کل ترافیک سیستم (نه فقط پروکسی‌محور) + مسیریابی
  برنامه‌محور واقعی (per-app tunneling) + UDP-over-TCP برای بازی.
- فشرده‌سازی انتخابی HTTP (gzip/br) برای حالت‌های کم‌بازده.
- چندگوشی: اشتراک هم‌زمان ۲ گوشی (aggregation ساده round-robin).

## v1.3 — اتوماسیون و مانیتورینگ
- لاگ‌برداری دائمی سیگنال (CSV) + نمودار ۲۴ ساعته + هشدار افت کیفیت.
- CLI و `netbridge://` deep-link برای اتوماسیون.
- Service ویندوزی جدا از UI (اجرای headless).

## v2.0 — ایده‌های بلندمدت
- WinUSB transport اختصاصی (حذف وابستگی به adb.exe برای دیتاپلین).
- پشتیبانی iOS (محدود به capabilities اپ‌استور).
- رمزنگاری لایه ۲ سفارشی روی کابل (HMAC + AES-CTR با کلید جفت‌سازی).

## نکات حقوقی
استفاده از تترینگ ممکن است مشمول سیاست اپراتور شما باشد؛ مسئولیت رعایت قوانین
اپراتور و کشور با کاربر است.
</div>

English: v1.1 = true band steering + OEM profiles + optional TLS bridge;
v1.2 = Wintun full-tunnel (system-wide capture, real per-app routing, UDP-over-TCP);
v1.3 = long-term signal logging, CLI, headless Windows service; v2 = custom WinUSB
transport, iOS research, link-layer encryption. Legal note: carrier TOS is the
user's responsibility.
