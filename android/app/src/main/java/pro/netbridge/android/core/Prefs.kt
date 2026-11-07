package pro.netbridge.android.core

import android.content.Context
import android.content.SharedPreferences
import java.security.SecureRandom

/**
 * Central settings store (SharedPreferences).
 *
 * All defaults match the Windows companion defaults; keep both sides in sync
 * (see docs/PROTOCOL.md).
 */
object Prefs {

    private const val FILE = "netbridge"

    const val DEFAULT_PROXY_PORT = 59110
    const val DEFAULT_BRIDGE_PORT = 59123
    const val DEFAULT_SIGNAL_INTERVAL_MS = 1000

    private lateinit var sp: SharedPreferences

    fun init(context: Context) {
        sp = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    }

    // ---- Token -------------------------------------------------------------

    /** 32 hex chars. Generated on first run, used by both proxy (SOCKS5 user/pass)
     *  and bridge (HMAC-SHA256 challenge/response). */
    val token: String
        get() {
            var t = sp.getString("token", null)
            if (t == null) {
                t = newToken()
                sp.edit().putString("token", t).apply()
            }
            return t
        }

    fun regenerateToken(): String {
        val t = newToken()
        sp.edit().putString("token", t).apply()
        return t
    }

    private fun newToken(): String {
        val bytes = ByteArray(16)
        SecureRandom().nextBytes(bytes)
        return bytes.joinToString("") { "%02x".format(it) }
    }

    // ---- Proxy engine ------------------------------------------------------

    var proxyPort: Int
        get() = sp.getInt("proxy_port", DEFAULT_PROXY_PORT)
        set(v) = sp.edit().putInt("proxy_port", v).apply()

    var bridgePort: Int
        get() = sp.getInt("bridge_port", DEFAULT_BRIDGE_PORT)
        set(v) = sp.edit().putInt("bridge_port", v).apply()

    /** When false the proxy binds loopback only (no tether data plane). */
    var bindAll: Boolean
        get() = sp.getBoolean("bind_all", true)
        set(v) = sp.edit().putBoolean("bind_all", v).apply()

    var requireAuth: Boolean
        get() = sp.getBoolean("require_auth", true)
        set(v) = sp.edit().putBoolean("require_auth", v).apply()

    /** Asked once for battery-optimization exemption (keeps the service alive). */
    var batteryPrompted: Boolean
        get() = sp.getBoolean("battery_prompted", false)
        set(v) = sp.edit().putBoolean("battery_prompted", v).apply()

    // ---- Signal ------------------------------------------------------------

    var signalIntervalMs: Int
        get() = sp.getInt("signal_interval", DEFAULT_SIGNAL_INTERVAL_MS)
        set(v) = sp.edit().putInt("signal_interval", v).apply()

    // ---- Service -----------------------------------------------------------

    var startOnBoot: Boolean
        get() = sp.getBoolean("start_on_boot", false)
        set(v) = sp.edit().putBoolean("start_on_boot", v).apply()

    /** Start the proxy service as soon as the app is opened (zero-touch). */
    var autoStartService: Boolean
        get() = sp.getBoolean("auto_start_service", true)
        set(v) = sp.edit().putBoolean("auto_start_service", v).apply()

    var language: String
        get() = sp.getString("language", "fa") ?: "fa"
        set(v) = sp.edit().putString("language", v).apply()
}
