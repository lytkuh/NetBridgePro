# NetBridge Pro — Security Model / مدل امنیتی

<div dir="rtl">

## هدف شما

«اشکال‌زدایی USB داخلی که فقط برنامه به آن دسترسی دارد، تا امنیت گوشی پایین نیاید و عمومی نباشد.»

اندروید اجازه نمی‌دهد یک اپ عادی `adbd` را خودش اجرا یا صاحبش شود؛ اما می‌توان **دقیقاً
همان نتیجه** را با سه لایه ساخت. NetBridge هر سه را پیاده کرده است:

## لایه ۱ — کلید اختصاصی + سرور adb خصوصی (حالت Companion)

- برنامه ویندوز سرور adb خودش را روی **درگاه 5038** (نه 5037 سیستم) با متغیر
  `ADB_VENDOR_KEYS` و کلید مخصوص در `ProgramData\NetBridge\keys\` بالا می‌آورد.
- روی گوشی فقط **کلید همین PC** مجاز است. هر PC دیگری پیام تأیید جدید می‌بیند.
- توصیه فعال‌سازی: یک بار در Developer options گزینه «Revoke USB debugging
  authorizations» را بزنید و فقط این PC را تأیید کنید → دسترسی قدیمی‌ها پاک می‌شود.
- Wireless debugging بعد از جفت‌سازی توسط ویزارد خاموش می‌شود
  (`settings put global adb_wifi_enabled 0`).

## لایه ۲ — حداقل‌گرایی در دسترسی‌ها

اپ اندروید فقط این‌ها را می‌خواهد (هر ۴ مورد با `pm grant` یک‌بار از PC):

| دسترسی | دلیل |
|---|---|
| `WRITE_SECURE_SETTINGS` | نوشتن `preferred_network_mode`، خواندن/خاموش‌کردن `adb_enabled` (قفل) |
| `READ_PHONE_STATE` | نوع رادیو، subscription فعال |
| `ACCESS_FINE_LOCATION` | جزئیات سل (PCI/TAC/Band) — الزام اندروید ۱۰+ |
| `POST_NOTIFICATIONS` | نوتیف سرویس Foreground |

هیچ درخواستی برای روت، شل دائمی، یا دسترسی‌های سیستمی وجود ندارد. **پل (Bridge) هیچ
دستور شل عمومی از PC اجرا نمی‌کند** — فقط یک لیست سفید ثابت از دستورات دارد.

## لایه ۳ — حالت قفل (Lockdown)

در تنظیمات ویندوز یا سوییچ «امنیت» روی گوشی:

- `adb_enabled=0` → **دیباگ USB سراسری خاموش**؛ هیچ کامپیوتری (حتی این PC) نمی‌تواند
  جلسه دیباگ باز کند.
- ادامه کار: پل و پراکسی از **رابط تترینگ USB** (NCM) سرویس می‌شوند که به adb ربطی ندارد.
- آنچه در قفل موقتاً از کار می‌افتد: چرخه هواپیما برای اعمال مود، تونل ADB، و فعال‌سازی
  تترینگ از PC (تترینگ را می‌توانید دستی روشن کنید).
- خروج از قفل: Developer options را روشن کنید و جفت‌سازی را تکرار کنید.

## احراز هویت ترافیک

- **پل**: چالش/پاسخ HMAC-SHA256 (proof = HMAC(token, clientNonce|serverNonce))؛
  مقایسه زمان-ثابت؛ قبل از verify هیچ دستوری پاسخ نمی‌گیرد.
- **پراکسی**: SOCKS5 RFC1929 (user=`netbridge`, pass=token) / HTTP Basic — همان توکن.
- LocalProxy روی PC فقط به `127.0.0.1` گوش می‌دهد (سایر دستگاه‌های LAN نمی‌توانند
  از تونل شما استفاده کنند).

## سخت‌گیری‌های کد

- مقایسه توکن‌ها constant-time در هر دو طرف.
- LocalProxy/پل: بدون تزریق — JSON با پارسر استاندارد؛ shell از PC فقط به adb اختصاصی
  و با رشته‌های ثابت می‌رود (به‌جز مقادیر عددی validated برای `settings put`).
- کلید adb هرگز از ProgramData خارج نمی‌شود؛ فقط اثر انگشت SHA-1 نمایش داده می‌شود.
- لاگ‌ها حاوی توکن نیستند.

## مدل تهدید و محدودیت‌های صادقانه

- کاربری که **دسترسی فیزیکی + کابل** به گوشی و PCِ جفت‌شده داشته باشد می‌تواند مثل
  هر adb معمولی شل بگیرد (این محدودیت ذاتی adb است). رمز قفل صفحه + عدم اتصال در
  مکان‌های ناامن، همان توصیه استاندارد گوگل است.
- در حالت Lockdown این سطح تهدید عملاً صفر می‌شود.
- ترافیک بین PC و گوشی روی USB رمزنگاری لایه انتقال ندارد؛ **پیلودهای TLS اپ‌ها**
  سالم‌اند، اما سوکت پل/پراکسی رمز نشده‌اند و توکن فقط برای auth است. روی کابل USB
  این ریسک ناچیز است؛ برای Wi-Fi داخلی خانه قابل‌قبول؛ **برای شبکه‌های Wi-Fi عمومی
  از حالت USB یا Lockdown+USB استفاده کنید.** (TLS سرتاسری برای پل در ROADMAP است.)

</div>

English summary: three layers — (1) dedicated adb server on port 5038 with a
PC-specific key (only this machine is authorized), (2) least-privilege Android
grants via one-time `pm grant` (WRITE_SECURE_SETTINGS + 3 runtime permissions,
no root, no shell service), and (3) a Lockdown mode that disables global
`adb_enabled` entirely while bridge/proxy keep working over the NCM tether
interface. Bridge uses HMAC-SHA256 challenge/response; proxy uses RFC1929 with
the same token; LocalProxy binds loopback only. Honest limits are listed above.
