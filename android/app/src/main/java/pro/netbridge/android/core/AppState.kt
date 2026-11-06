package pro.netbridge.android.core

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/**
 * Process-wide observable state. The foreground service writes into it, the
 * Compose UI observes it, and the bridge reads from it. No singletons beyond
 * this one and the Prefs/Logger objects.
 */
data class SignalSample(
    val timestampMs: Long,
    val networkType: String,     // e.g. LTE, NR, HSPAP, EDGE, UNKNOWN
    val nrState: String,         // NONE / NOT_RESTRICTED / CONNECTED (NSA/SA indicator)
    val rsrp: Int,               // dBm, LTE/NR, Int.MIN_VALUE when unavailable
    val rsrq: Int,               // dB
    val rssi: Int,               // dBm
    val sinr: Int,               // dB (NR SS-SINR / LTE CQI-derived estimate)
    val cqi: Int,
    val band: String,            // best-effort band string ("B3", "n78", earfcn fallback)
    val pci: Int,
    val tac: String,
    val cellId: String,
    val operator: String,
    val isDataConnected: Boolean,
    val technologyFamily: String // 2G / 3G / 4G / 5G / WLAN / UNKNOWN
)

data class ProxyStatsSnapshot(
    val running: Boolean = false,
    val port: Int = 0,
    val bindAll: Boolean = true,
    val activeConnections: Int = 0,
    val totalConnections: Long = 0,
    val rxBytes: Long = 0,
    val txBytes: Long = 0,
    val rxRateBps: Long = 0,     // 1-second EMA of throughput
    val txRateBps: Long = 0
)

object AppState {

    // ---- Proxy engine ------------------------------------------------------
    private val _proxyStats = MutableStateFlow(ProxyStatsSnapshot())
    val proxyStats: StateFlow<ProxyStatsSnapshot> = _proxyStats

    fun setProxyStats(s: ProxyStatsSnapshot) { _proxyStats.value = s }

    // ---- Signal ------------------------------------------------------------
    private val _signal = MutableStateFlow<SignalSample?>(null)
    val signal: StateFlow<SignalSample?> = _signal

    fun setSignal(s: SignalSample?) { _signal.value = s }

    /** Live event counter bumped by SignalMonitor; bridge subscribers poll deltas. */
    val signalVersion = MutableStateFlow(0L)

    // ---- Tethering ---------------------------------------------------------
    private val _tetherActive = MutableStateFlow(false)
    val tetherActive: StateFlow<Boolean> = _tetherActive

    fun setTetherActive(v: Boolean) { _tetherActive.value = v }

    // ---- Bridge ------------------------------------------------------------
    private val _bridgeConnected = MutableStateFlow(false)
    val bridgeConnected: StateFlow<Boolean> = _bridgeConnected

    fun setBridgeConnected(v: Boolean) { _bridgeConnected.value = v }

    // ---- Service -----------------------------------------------------------
    private val _serviceRunning = MutableStateFlow(false)
    val serviceRunning: StateFlow<Boolean> = _serviceRunning

    fun setServiceRunning(v: Boolean) { _serviceRunning.value = v }
}
