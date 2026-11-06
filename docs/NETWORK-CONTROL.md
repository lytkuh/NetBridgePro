# NetBridge Pro — RAN / Network Mode Control / کنترل شبکه رادیویی

<div dir="rtl">

## آنچه واقعاً بدون روت ممکن است

| قابلیت | وضعیت | سازوکار |
|---|---|---|
| قفل نسل (فقط 2G/3G/4G/5G، ترکیب‌ها) | ✅ بدون روت | نوشتن `preferred_network_modeN` + اعمال با چرخه هواپیما |
| نمایش باند/PCI/TAC/CellID/RSRP/SINR | ✅ بدون روت (اجازه Location) | `CellInfo`/`SignalStrength` |
| تشخیص 5G NSA/SA | ✅ | `ServiceState.nrState` |
| سوییچ خودکار هوشمند | ✅ | موتور PC روی جریان سیگنال |
| **قفل باند مشخص (مثلاً فقط B3)** | ⚠️ تجربی/نیازمند روت یا ابزار OEM | API استاندارد ندارد؛ برنامه هدایت می‌کند |
| قفل دکل (Cell lock) | ❌ نیازمند روت/مدیتیشن مودم | در حد ابزارهای OEM (QXDM و...) |

## مقدارها (RILConstants)

`preferred_network_modeN` (N = شماره اسلات + ۱) از `Settings.Global`:

| حالت | مقدار(های) رایج | توضیح |
|---|---|---|
| پیش‌فرض دستگاه | `0` | WCDMA-preferred (بسیاری از فیرم‌ورها) |
| فقط 2G | `1` | GSM only — قابل‌اتکا |
| فقط 3G | `2` | WCDMA only |
| 3G+2G | `3`, `0` | GSM/WCDMA auto |
| 4G خودکار | `9`, `12` | LTE/GSM/WCDMA |
| فقط 4G | `11` | LTE only — قابل‌اتکا |
| 5G+4G | `18`, `22`, `26` | NR/LTE |
| فقط 5G | `17` | NR only |
| 5G خودکار | `26`, `22`, `33`, `20`, `9` | OEM-dependent |

**چرا چند کاندید؟** عدد صحیح بین سازندگان (سامسونگ/شیائومی/پیکسل/مدیاتک) فرق می‌کند.
NetBridge به‌جای حدس کور، **می‌نویسد → می‌سنجد → در صورت عدم اعمال، کاندید بعدی**:

1. نوشتن از پل (`WRITE_SECURE_SETTINGS`) یا `adb settings put global ...`
2. ۶ ثانیه صبر؛ خواندن `networkType` + `nrState` از پل
3. مطابق نبود → چرخه هواپیما (`cmd connectivity airplane-mode enable/disable`) از adb اختصاصی
4. ۱۲ ثانیه صبر و راستی‌آزمایی مجدد؛ در صورت شکست، کاندید بعدی
5. هیچ‌کدام → پیام شفاف «این فیرم‌ور این مقدار را نمی‌پذیرد»

برای «فقط 5G» راستی‌آزمایی سخت‌گیرانه است: باید `nrState ∈ {CONNECTED, NOT_RESTRICTED}` شود،
چون NSA در `networkType` گاهی LTE گزارش می‌شود.

## رفتار رادیو در حین چرخه هواپیما

- ۳.۵ ثانیه خاموش → روشن؛ ثبت‌مجدد معمولاً ۵–۱۵ ثانیه طول می‌کشد.
- در این بازه اینترنت گوشی/تونل قطع می‌شود؛ LocalProxy اتصال‌های TCP موجود را نگه
  می‌دارد ولی بسته‌ها صف/ری‌ترنس می‌شوند.
- سوییچر هوشمند بین چرخه‌ها حداقل ۱۸۰ ثانیه فاصله می‌گذارد (cooldown) و بعد از هر
  تغییر دستی ۱۰ دقیقه دست آدم را باز می‌گذارد.

## سوییچر هوشمند — منطق

```
هر ۱ ثانیه (جریان SignalData):
  stability:  NR فعال و RSRP < بد (پیش‌فرض -118) به مدت ۴۵s  → فقط 4G
              بعد از بهبود RSRP > خوب (پیش‌فرض -105) به مدت ۶۰s → 5G خودکار
  speed:      اگر قفل 4G و شرایط خوب → بازگشت به 5G خودکار
  battery:    RSRP بد + SINR منفی → 4G خودکار (کاهش اسکن/ثبت‌مجدد)
محافظت‌ها: cooldown ۱۸۰s، min-dwell ۱۲۰s، توقف بعد از تغییر دستی ۱۰ دقیقه
```

آستانه‌ها و زمان‌ها از صفحه «شبکه و دکل» قابل تغییر و در `settings.json` ماندگارند.

## قفل باند (تجربی)

API عمومی برای band-lock وجود ندارد. آنچه برنامه اکنون می‌دهد:

- **نمایش باند فعلی** (Bx / earfcn / nXXX) به‌صورت زنده — می‌توانید دستی از منوهای سرویس
  OEM (*#2263# سامسونگ، منوی RadioInfo پیکسل با `*#*#4636#*#*`، …) باند را قفل کنید و
  NetBridge نتیجه را مانیتور کند.
- مسیر شبه‌قفل: قفل نسل + چرخه ثبت‌مجدد (هواپیما) وقتی باند خارج از لیست مجاز دیده شد —
  در ROADMAP به‌صورت «Band Steering» اضافه می‌شود؛ با روت می‌توان از OEM props استفاده کرد.

## جدول عیب‌یابی

| نشانه | علت محتمل | راه‌حل |
|---|---|---|
| نوشتن مود ok ولی رادیو تغییر نکرد | فیرم‌ور بدون ناظر settings | چرخه هواپیما خودکار انجام می‌شود؛ صبر کنید |
| همه کاندیدها رد شدند | جدول OEM متفاوت | مقدار دلخواه (0-33) را از UI تست کنید |
| `nrState` همیشه NONE | NSA واقعاً فعال نیست / منطقه 5G ندارد | با اپراتور/منطقه چک کنید |
| چرخه هواپیما کار نمی‌کند | حالت Lockdown | موقتاً Companion کنید |

</div>

English: same content summarized — non-root mode locks via
`preferred_network_mode` + verification-driven candidate list + airplane cycle over
the private adb channel; live band/PCI/NR-state telemetry; smart switcher policies
with hysteresis; band lock is honestly documented as experimental (needs root/OEM
tools); troubleshooting table included.
