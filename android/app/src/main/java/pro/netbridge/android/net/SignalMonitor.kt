package pro.netbridge.android.net

import android.content.Context
import android.telephony.CellIdentityLte
import android.telephony.CellIdentityNr
import android.telephony.CellInfo
import android.telephony.CellInfoLte
import android.telephony.CellInfoNr
import android.telephony.CellSignalStrengthLte
import android.telephony.CellSignalStrengthNr
import android.telephony.PhoneStateListener
import android.telephony.ServiceState
import android.telephony.SignalStrength
import android.telephony.TelephonyDisplayInfo
import android.telephony.TelephonyManager
import android.os.Build
import pro.netbridge.android.core.AppState
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Reflect
import pro.netbridge.android.core.SignalSample
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

/**
 * Live radio telemetry: network type, NR state (NSA/SA), RSRP/RSRQ/SINR,
 * band, PCI, TAC and cell identity. Requires READ_PHONE_STATE and, on
 * Android 10+, ACCESS_FINE_LOCATION for cell identity details.
 */
class SignalMonitor(private val context: Context) {

    private val running = AtomicBoolean(false)
    private var listener: PhoneStateListener? = null
    private var lastEmitMs = AtomicLong(0)
    @Volatile private var lastServiceState: ServiceState? = null
    @Volatile private var lastDisplayInfo: TelephonyDisplayInfo? = null
    @Volatile private var lastSignalStrength: SignalStrength? = null
    @Volatile private var lastCellInfo: List<CellInfo>? = null

    @Volatile var subscriptionIds: IntArray = IntArray(0)
        private set

    private val TAG = "Signal"

    fun start(intervalMs: Int) {
        if (!running.compareAndSet(false, true)) return
        val tm = context.getSystemService(Context.TELEPHONY_SERVICE) as TelephonyManager
        try {
            subscriptionIds = if (Build.VERSION.SDK_INT >= 30) {
                intArrayOf(tm.subscriptionId)
            } else {
                val sid = Reflect.call(tm, "getSubscriptionId") as? Int
                if (sid != null && sid >= 0) intArrayOf(sid) else IntArray(0)
            }
        } catch (t: Throwable) {
            subscriptionIds = IntArray(0)
        }

        listener = object : PhoneStateListener() {
            override fun onSignalStrengthsChanged(signalStrength: SignalStrength?) {
                if (signalStrength != null) lastSignalStrength = signalStrength
                emit(tm, intervalMs, signalStrength)
            }

            override fun onServiceStateChanged(serviceState: ServiceState?) {
                if (serviceState != null) lastServiceState = serviceState
                emit(tm, intervalMs, null)
            }

            override fun onCellInfoChanged(cellInfo: MutableList<CellInfo>?) {
                if (cellInfo != null) lastCellInfo = cellInfo
                emit(tm, intervalMs, null, cellInfo)
            }

            override fun onDisplayInfoChanged(telephonyDisplayInfo: TelephonyDisplayInfo) {
                lastDisplayInfo = telephonyDisplayInfo
                emit(tm, intervalMs, null)
            }

            override fun onDataConnectionStateChanged(state: Int, networkType: Int) {
                emit(tm, intervalMs, null)
            }
        }
        try {
            tm.listen(
                listener,
                PhoneStateListener.LISTEN_SIGNAL_STRENGTHS
                        or PhoneStateListener.LISTEN_SERVICE_STATE
                        or PhoneStateListener.LISTEN_CELL_INFO
                        or PhoneStateListener.LISTEN_DATA_CONNECTION_STATE
                        or PhoneStateListener.LISTEN_DISPLAY_INFO_CHANGED
            )
            Logger.i(TAG, "Signal listener registered (interval=${intervalMs}ms)")
        } catch (e: Exception) {
            Logger.e(TAG, "listen failed", e)
            running.set(false)
        }
    }

    fun stop() {
        if (!running.compareAndSet(true, false)) return
        try {
            val tm = context.getSystemService(Context.TELEPHONY_SERVICE) as TelephonyManager
            tm.listen(listener, PhoneStateListener.LISTEN_NONE)
        } catch (_: Exception) {}
        listener = null
    }

    /** Force an immediate sample (bridge "net.type" command). */
    fun sampleNow(): SignalSample? {
        val tm = context.getSystemService(Context.TELEPHONY_SERVICE) as TelephonyManager
        return buildSample(tm, null, null)
    }

    private fun emit(tm: TelephonyManager, intervalMs: Int, ss: SignalStrength?, cells: List<CellInfo>? = null) {
        val now = System.currentTimeMillis()
        val last = lastEmitMs.get()
        if (now - last < intervalMs) return
        if (!lastEmitMs.compareAndSet(last, now)) return
        try {
            val sample = buildSample(tm, ss, cells)
            AppState.setSignal(sample)
            AppState.signalVersion.value = AppState.signalVersion.value + 1
        } catch (e: Exception) {
            Logger.d(TAG, "emit failed: ${e.message}")
        }
    }

    private fun buildSample(tm: TelephonyManager, ssParam: SignalStrength?, cellsParam: List<CellInfo>?): SignalSample {
        val ss = ssParam ?: lastSignalStrength
        val cells = cellsParam ?: lastCellInfo

        val typeName = try {
            NetworkTypes.nameOf(tm.dataNetworkType)
        } catch (e: Exception) { "UNKNOWN" }

        val nrState = try {
            // Public API (31+): TelephonyDisplayInfo.overrideNetworkType tells us
            // whether the NR link is NSA/SA/advanced. getNrState() itself is
            // @SystemApi, so fall back to reflection on ServiceState.
            val di = lastDisplayInfo
            val viaDisplay = if (di != null) when (di.overrideNetworkType) {
                TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NR_ADVANCED -> "NR_ADVANCED" // SA + high band
                TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NR_NSA_MMWAVE -> "NSA_MMWAVE"
                TelephonyDisplayInfo.OVERRIDE_NETWORK_TYPE_NR_NSA -> "NSA"
                else -> null
            } else null
            viaDisplay ?: lastServiceState?.let {
                when (Reflect.call(it, "getNrState")) {
                    android.telephony.NetworkRegistrationInfo.NR_STATE_CONNECTED -> "CONNECTED"
                    android.telephony.NetworkRegistrationInfo.NR_STATE_NOT_RESTRICTED -> "NOT_RESTRICTED"
                    android.telephony.NetworkRegistrationInfo.NR_STATE_RESTRICTED -> "RESTRICTED"
                    else -> "NONE"
                }
            } ?: "NONE"
        } catch (e: Exception) { "NONE" }

        var rsrp = Int.MIN_VALUE
        var rsrq = Int.MIN_VALUE
        var rssi = Int.MIN_VALUE
        var sinr = Int.MIN_VALUE
        var cqi = Int.MIN_VALUE
        var band = ""
        var pci = Int.MIN_VALUE
        var tac = ""
        var cellId = ""

        // Signal strength metrics
        if (ss != null) {
            try {
                for (css in ss.cellSignalStrengths) {
                    when (css) {
                        is CellSignalStrengthNr -> {
                            rsrp = orBetter(rsrp, css.ssRsrp)
                            rsrq = orBetter(rsrq, css.ssRsrq)
                            sinr = orBetter(sinr, css.ssSinr)
                        }
                        is CellSignalStrengthLte -> {
                            rsrp = orBetter(rsrp, css.rsrp)
                            rsrq = orBetter(rsrq, css.rsrq)
                            rssi = orBetter(rssi, css.rssi)
                            cqi = orBetter(cqi, Reflect.call(css, "getCqi") as? Int ?: Int.MIN_VALUE)
                        }
                        else -> {
                            val dbm = css.dbm
                            if (dbm in -120..0) rssi = orBetter(rssi, dbm)
                        }
                    }
                }
            } catch (e: Exception) {
                // API < 29: no per-technology breakdown; SignalStrength.getDbm()
                // is @UnsupportedAppUsage, so reflect it, else estimate from bars.
                val dbm = Reflect.call(ss, "getDbm") as? Int ?: (-113 + 10 * ss.level)
                rssi = orBetter(rssi, dbm)
            }
        }

        // Cell identity details (needs location permission on API 30+)
        if (cells != null) {
            for (ci in cells) {
                try {
                    val primary = Build.VERSION.SDK_INT >= 28 &&
                        ci.cellConnectionStatus == CellInfo.CONNECTION_PRIMARY_SERVING
                    when {
                        ci is CellInfoLte && primary -> {
                            val id = ci.cellIdentity as CellIdentityLte
                            pci = id.pci
                            tac = id.tac.takeIf { it != Int.MAX_VALUE }?.toString() ?: tac
                            cellId = id.ci.takeIf { it != Int.MAX_VALUE }?.toString() ?: cellId
                            val b = Reflect.call(id, "getBand") as? Int
                            val earfcn = Reflect.call(id, "getEarfcn") as? Int
                            band = b?.let { "B$it" } ?: earfcn?.let { "earfcn:$it" } ?: band
                        }
                        Build.VERSION.SDK_INT >= 29 && ci is CellInfoNr && primary -> {
                            val id = ci.cellIdentity as? CellIdentityNr
                            if (id != null) {
                                pci = id.pci
                                tac = id.tac.takeIf { it != Int.MAX_VALUE && it != -1 }?.toString() ?: tac
                                cellId = id.nci.takeIf { it != Long.MAX_VALUE && it != -1L }?.toString() ?: cellId
                                val nrarfcn = Reflect.call(id, "getNrarfcn") as? Int
                                band = nrarfcn?.let { "n$it" } ?: band
                            }
                        }
                    }
                } catch (_: Exception) {}
            }
        }

        val operator = try {
            lastServiceState?.operatorNumeric?.let { "${lastServiceState?.operatorAlphaLong ?: ""} ($it)" }
                ?: tm.networkOperatorName
        } catch (e: Exception) { "" }

        return SignalSample(
            timestampMs = System.currentTimeMillis(),
            networkType = typeName,
            nrState = nrState,
            rsrp = rsrp, rsrq = rsrq, rssi = rssi, sinr = sinr, cqi = cqi,
            band = band, pci = pci, tac = tac, cellId = cellId,
            operator = operator,
            isDataConnected = tm.dataState == TelephonyManager.DATA_CONNECTED,
            technologyFamily = NetworkTypes.familyOf(typeName)
        )
    }

    private fun orBetter(cur: Int, candidate: Int): Int = when {
        candidate == Int.MIN_VALUE -> cur
        cur == Int.MIN_VALUE -> candidate
        // "better" = closer to zero (stronger) for dBm-ish metrics
        kotlin.math.abs(candidate) < kotlin.math.abs(cur) -> candidate
        else -> cur
    }
}
