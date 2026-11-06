# NetBridge Pro — Architecture / معماری

<div dir="rtl">

## نمای کلی دو صفحه‌ای (Two-plane design)

NetBridge عمداً **مسیر داده** و **مسیر کنترل** را از هم جدا می‌کند:

```
┌─────────────────────────── Windows 11 (WPF, .NET 8) ───────────────────────────┐
│                                                                                │
│  برنامه‌های ویندوز ──► LocalProxy(127.0.0.1:59120) ─┬─► [bypass list] → مستقیم  │
│   (پروکسی سیستمی WinINET یا SOCKS/HTTP دستی)        └─► phone SOCKS5 ─┐        │
│  AppFirewall (WFP rules)      DoH/کش DNS        SpeedTest ───────────┤        │
│  NetModeController ──► adb(5038) ── shell ──────────────────────────┤ کنترل  │
│  SmartSwitcher  ◄── BridgeClient(59123, HMAC) ◄── SignalMonitor ◄───┘        │
└────────────────────────────────┬───────────────────────────────────────────────┘
                     ┌───────────┴───────────┐
                     │  Transport (انتخابی)  │
                     │ 1) USB-NCM تترینگ     │  ← سریع‌ترین
                     │ 2) ADB forward تونل   │  ← بدون نیاز به تترینگ
                     │ 3) Wi-Fi هات‌اسپات    │
                     │ 4) Bluetooth PAN      │
                     └───────────┬───────────┘
┌────────────────────────────────┴───────────────────────────────────────────────┐
│                        Android (Kotlin, Compose, foreground svc)                │
│  MultiProxyServer(:59110) — SOCKS5(RFC1928/1929) + UDP ASSOCIATE + HTTP proxy  │
│  BridgeServer(:59123)     — JSON lines، چالش HMAC-SHA256، دستورات کنترلی       │
│  SignalMonitor            — RSRP/RSRQ/SINR/band/PCI/NR-state (PhoneStateListener)│
│  PhoneNet                 — خواندن/نوشتن preferred_network_mode (WSS)، adb_enabled│
└────────────────────────────────────────────────────────────────────────────────┘
```

### چرا این طراحی سریع‌تر از تترینگ پیش‌فرض ویندوز است؟

1. تترینگ داخلی ویندوز IP هر بسته را NAT می‌کند و MTU/تنظیماتش عمومی است؛ اینجا
   ترافیک به‌صورت TCP استریم در سطح **پراکسی** منتقل می‌شود: بافر ۲۵۶K سوکت،
   TCP_NODELAY، رله ۶۴K با دو کوروتین/تسک، بدون لایه NAT اضافی روی PC.
2. TLS پایان‌به‌انتها از اپ‌ها حفظ می‌شود (پراکسی فقط CONNECT می‌کند)؛ هزینه رمزنگاری
   مضاعف وجود ندارد.
3. در حالت NCM، مسیر USB خطِ مستقیم به مودم گوشی است و پیکربندی QoS/پنجره TCP توسط
   خود اندروید (بهینه مودم سازنده) انجام می‌شود.

## جریان اتصال (Connection sequence)

1. `ConnectionManager.ConnectAsync`
2. (حالت companion) اجرای سرور adb اختصاصی روی **5038** با `ADB_VENDOR_KEYS` مخصوص.
3. اگر تترینگ USB فعال نیست: `svc usb setFunctions ncm` (fallback: `rndis`) از shell.
4. کاوش endpoint: برای هر آداپتور فعال (NCM → BT PAN → Wi-Fi) gatewayها روی 59110 پروب
   می‌شوند؛ اولین IPای که پاسخ دهد = گوشی. (بدون اتکا به نام آداپتور)
5. اگر هیچ مسیری نبود و adb داریم: `forward tcp:59121→59110` و `tcp:59122→59123`.
6. `LocalProxy.SetPhoneEndpoint` + اتصال Bridge (HMAC) + آمار گوشی.
7. تنظیم پروکسی سیستم WinINET روی `127.0.0.1:59120` (با پشتیبان‌گیری و بازگردانی).
8. حلقه مانیتور ۱ثانیه‌ای: آمار، سیگنال (هر ۲s)، liveness (هر ۱۰s) → در صورت قطع،
   Disconnect تمیز + تلاش مجدد خودکار.

## مسیر داده در LocalProxy

- تشخیص پروتکل با اولین بایت: `0x04/0x05` → SOCKS4/4a/5، در غیر این‌صورت HTTP proxy.
- SOCKS5: NO-AUTH برای کلاینت‌های loopback (خود LocalProxy احراز را سمت گوشی انجام می‌دهد)؛
  CONNECT و UDP ASSOCIATE پشتیبانی می‌شود.
- HTTP: CONNECT + absolute-form (مسیر WinINET برای سایت‌های plain-HTTP) با حذف هدرهای
  hop-by-hop و `Connection: close`.
- قواعد: `BypassDomains` (مستقیم از PC)، DNS: `remote` (حل روی گوشی) / `local-doh` (DoH
  + کش TTL) / `system`.
- آمار: هر اتصال یک Record (host/port/rx/tx) + مجموع و نرخ EMA یک‌ثانیه‌ای.

## اندروید — MultiProxyServer

- `ServerSocket(backlog)` + یک کوروتین per-connection روی `Dispatchers.IO`.
- SO_RCVBUF/SNDBUF=256K، TCP_NODELAY، soTimeout=30s (پس از CONNECT صفر).
- SOCKS5 کامل: متد 0x00/0x02، RFC1929 با مقایسه زمان-ثابت، CONNECT + UDP ASSOCIATE
  (header parse A/AAAA/domain، reply با BND.ADDR هم‌زیرشبکه کلاینت).
- UDP associate: نقشه ساده «آخرین کلاینت»، فریم‌بندی استاندارد RSV/FRAG/ATYP، مسیر برگشت
  encapsulate شده.

## اندروید — BridgeServer

- یک خط = یک JSON؛ پاسخ‌ها `{"id","ok","result"|"error"}`؛ نیازمند auth قبل از هر دستور.
- auth: `auth` (nonce سرور) → `auth.verify` (proof = HMAC(token, clientNonce|serverNonce)).
- دستورات: `device.info`, `net.info`, `net.mode.read/write`, `net.type`, `net.signal.*`,
  `proxy.stats`, `tether.status`, `logs.recent`, `ping`.
- نکته امنیتی: **هیچ `shell.exec` عمومی وجود ندارد**؛ PC فقط از adb اختصاصی شل می‌گیرد و
  پل فقط دستورات سفارشی (whitelist) را اجرا می‌کند.

## کنترل مود شبکه (خلاصه؛ جزئیات در NETWORK-CONTROL.md)

1. نوشتن `preferred_network_modeN` از پل (WRITE_SECURE_SETTINGS) یا `adb settings put`.
2. صبر ۶ ثانیه + راستی‌آزمایی از `net.type` (family + nrState).
3. اگر رادیو خودش سوییچ نکرد: چرخه `cmd connectivity airplane-mode` از adb اختصاصی.
4. کاندیدهای بعدی (۲۶/۲۲/۳۳/۲۰ برای 5G-Auto و ...) تا تأیید.

## سوییچر هوشمند

- ورودی: `SignalData` هر ۱ ثانیه (rsrp, sinr, nrState).
- سیاست‌ها: stability (سقوط NR ضعیف → LTE-only؛ بازگشت با بهبود)، speed (بازگشت به 5G-Auto)،
  battery (رادیوی ضعیف → 4G-Auto).
- محافظت‌ها: cooldown (پیش‌فرض ۱۸۰s)، min-dwell (۱۲۰s)، توقف ۱۰دقیقه‌ای بعد از تغییر دستی.

## ویندوز — یادداشت‌های پیاده‌سازی

- **بدون هیچ پکیج NuGet خارجی** (فقط BCL + WPF) → بیلد قابل‌تکرار و بی‌دردسر.
- ADB: کلاینتِ پروتکل **سرور** adb (نه پروتکل USB)؛ سرور اختصاصی با پورت/کلید جدا از adb
  سیستم اجرا می‌شود؛ دانلود خودکار platform-tools از dl.google.com.
- AppFirewall: COM `HNetCfg.FwPolicy2` با قواعد `NetBridge block *`، ماندگار در ProgramData.
- SystemProxySetter: رجیستری WinINET + `InternetSetOption(SETTINGS_CHANGED/REFRESH)`.

</div>

English: the same document in short — a two-plane design (data plane = SOCKS5/HTTP
proxy over the fastest of 4 transports; control plane = authenticated JSON bridge +
private adb server). Connection discovery is probe-based (whoever answers on 59110
with our auth IS the phone). Windows side has zero external NuGet dependencies.
