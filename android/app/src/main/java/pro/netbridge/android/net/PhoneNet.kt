package pro.netbridge.android.net

import android.content.Context
import android.os.Build
import android.telephony.SubscriptionManager
import android.telephony.TelephonyManager
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Reflect

/**
 * Phone-side helpers around tethering state and SIM bookkeeping.
 * Actual tethering enable/disable runs on the PC side via shell
 * (`svc usb setFunctions ncm|rndis`) because it needs shell privileges.
 */
object PhoneNet {

    private const val TAG = "PhoneNet"

    /** Is a USB tethering interface (ncm/rndis) currently up? */
    fun isUsbTethered(context: Context): Boolean {
        try {
            if (Build.VERSION.SDK_INT >= 30) {
                // TetheringManager is @SystemApi (not in the public SDK);
                // go through reflection so this compiles against android.jar.
                val teth = context.getSystemService("tethering")
                if (teth != null) {
                    val ifaces = Reflect.call(teth, "getTetheredIfaces") as? Array<String>
                    if (ifaces != null) {
                        for (name in ifaces) {
                            if (name.startsWith("ncm") || name.startsWith("rndis") ||
                                name.startsWith("usb") || name.startsWith("tap")
                            ) return true
                        }
                    }
                }
            }
        } catch (_: Throwable) {}
        // Fallback: interface scan
        try {
            val nis = java.net.NetworkInterface.getNetworkInterfaces()
            for (ni in nis) {
                val n = ni.name.lowercase()
                if ((n.startsWith("ncm") || n.startsWith("rndis")) && ni.isUp) return true
            }
        } catch (_: Exception) {}
        return false
    }

    /** Active data subscription info: [subId, phoneIndex, slotName]. */
    fun activeSubscriptions(context: Context): List<Triple<Int, Int, String>> {
        val out = mutableListOf<Triple<Int, Int, String>>()
        try {
            val sm = context.getSystemService(Context.TELEPHONY_SUBSCRIPTION_SERVICE) as SubscriptionManager
            if (android.os.Build.VERSION.SDK_INT >= 30) {
                // @Nullable in the SDK — guard before iterating.
                val list = sm.activeSubscriptionInfoList
                if (list != null) {
                    for (info in list) {
                        out.add(Triple(info.subscriptionId, info.simSlotIndex, info.carrierName?.toString() ?: ""))
                    }
                }
            } else {
                val list = Reflect.call(sm, "getActiveSubscriptionInfoList") as? List<*>
                list?.forEach { info ->
                    val sid = Reflect.call(info!!, "getSubscriptionId") as? Int
                    val slot = Reflect.call(info, "getSimSlotIndex") as? Int
                    val carrier = Reflect.call(info, "getCarrierName") as? String
                    if (sid != null && slot != null) out.add(Triple(sid, slot, carrier ?: ""))
                }
            }
        } catch (e: Exception) {
            Logger.d(TAG, "activeSubscriptions failed: ${e.message}")
        }
        if (out.isEmpty()) out.add(Triple(-1, 0, "default"))
        return out
    }

    /** Current data network type name for a given subscription (or default). */
    fun networkTypeName(context: Context, subId: Int = -1): String {
        return try {
            val tm = context.getSystemService(Context.TELEPHONY_SERVICE) as TelephonyManager
            val target = if (subId >= 0 && Build.VERSION.SDK_INT >= 24) {
                tm.createForSubscriptionId(subId)
            } else tm
            @Suppress("DEPRECATION")
            NetworkTypes.nameOf(target.networkType)
        } catch (e: Exception) {
            "UNKNOWN"
        }
    }

    /** Read preferred_network_modeN directly from Settings.Global. */
    fun readPreferredMode(context: Context, phoneIndex: Int): Int? {
        return try {
            android.provider.Settings.Global.getInt(
                context.contentResolver, "preferred_network_mode${phoneIndex + 1}"
            )
        } catch (e: Exception) {
            try {
                android.provider.Settings.Global.getInt(
                    context.contentResolver, "preferred_network_mode"
                )
            } catch (e2: Exception) { null }
        }
    }

    /** Write preferred_network_modeN (needs WRITE_SECURE_SETTINGS). */
    fun writePreferredMode(context: Context, phoneIndex: Int, value: Int): Boolean {
        return try {
            android.provider.Settings.Global.putInt(
                context.contentResolver, "preferred_network_mode${phoneIndex + 1}", value
            )
        } catch (e: Exception) {
            Logger.w(TAG, "writePreferredMode failed: ${e.message}")
            false
        }
    }

    /** adb_enabled current state. */
    fun isAdbEnabled(context: Context): Boolean = try {
        android.provider.Settings.Global.getInt(context.contentResolver, "adb_enabled", 0) == 1
    } catch (e: Exception) { false }

    /** Toggle adb_enabled (needs WRITE_SECURE_SETTINGS) — the "Lockdown" switch. */
    fun setAdbEnabled(context: Context, enabled: Boolean): Boolean = try {
        android.provider.Settings.Global.putInt(
            context.contentResolver, "adb_enabled", if (enabled) 1 else 0
        )
    } catch (e: Exception) {
        Logger.w(TAG, "setAdbEnabled failed: ${e.message}")
        false
    }

    fun hasWriteSecureSettings(context: Context): Boolean = try {
        context.checkCallingOrSelfPermission(android.Manifest.permission.WRITE_SECURE_SETTINGS) ==
            android.content.pm.PackageManager.PERMISSION_GRANTED
    } catch (e: Exception) { false }

    fun batteryLevel(context: Context): Int = try {
        val intent = context.registerReceiver(null, android.content.IntentFilter(android.content.Intent.ACTION_BATTERY_CHANGED))
        val level = intent?.getIntExtra(android.os.BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = intent?.getIntExtra(android.os.BatteryManager.EXTRA_SCALE, -1) ?: -1
        if (level in 0..100 && scale > 0) level * 100 / scale else level
    } catch (e: Exception) { -1 }

    fun batteryTempC(context: Context): Double = try {
        val intent = context.registerReceiver(null, android.content.IntentFilter(android.content.Intent.ACTION_BATTERY_CHANGED))
        (intent?.getIntExtra(android.os.BatteryManager.EXTRA_TEMPERATURE, Int.MIN_VALUE)
            ?: Int.MIN_VALUE).let { if (it == Int.MIN_VALUE) -1.0 else it / 10.0 }
    } catch (e: Exception) { -1.0 }
}
