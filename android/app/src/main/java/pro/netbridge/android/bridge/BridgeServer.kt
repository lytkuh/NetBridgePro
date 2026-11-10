package pro.netbridge.android.bridge

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import android.content.Context
import pro.netbridge.android.BuildConfig
import pro.netbridge.android.core.AppState
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Prefs
import pro.netbridge.android.core.TokenAuth
import pro.netbridge.android.net.PhoneNet
import pro.netbridge.android.net.SignalMonitor
import pro.netbridge.android.proxy.ProxyStats
import java.io.BufferedReader
import java.io.IOException
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

/**
 * Control plane bridge. newline-delimited JSON over TCP with an
 * HMAC-SHA256 challenge/response login (see docs/PROTOCOL.md).
 *
 * Every response carries the request id: {"id":N,"ok":bool,"result":...| "error":"..."}
 * Events (signal/log) are pushed as {"event":"...","data":...}.
 */
class BridgeServer(
    private val context: Context,
    private val stats: ProxyStats,
    private val signalMonitor: SignalMonitor
) {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val running = AtomicBoolean(false)
    private var serverSocket: ServerSocket? = null
    private val sessions = AtomicLong(0)
    private var restartAttempts = 0

    companion object {
        private const val TAG = "Bridge"
        private const val PROTOCOL_VERSION = 1

        fun deviceInfoJson(): JSONObject {
            return JSONObject()
                .put("device", android.os.Build.MODEL ?: "")
                .put("brand", android.os.Build.BRAND ?: "")
                .put("manufacturer", android.os.Build.MANUFACTURER ?: "")
                .put("androidVersion", android.os.Build.VERSION.RELEASE ?: "")
                .put("sdkInt", android.os.Build.VERSION.SDK_INT)
                .put("appVersion", BuildConfig.VERSION_NAME)
                .put("protocol", PROTOCOL_VERSION)
        }
    }

    fun start() {
        restartAttempts = 0 // every explicit start gets a fresh watchdog budget
        if (!running.compareAndSet(false, true)) return
        scope.launch {
            // Watchdog: a dead accept loop means the companion can never pair —
            // restart the listener up to 6 times before giving up.
            while (running.get() && scope.isActive && restartAttempts < 6) {
                try {
                    val ss = ServerSocket()
                    ss.reuseAddress = true
                    ss.bind(InetSocketAddress("0.0.0.0", Prefs.bridgePort))
                    serverSocket = ss
                    Logger.i(TAG, "Bridge listening on 0.0.0.0:${Prefs.bridgePort}")
                    while (running.get() && scope.isActive) {
                        val client = try {
                            ss.accept()
                        } catch (e: IOException) {
                            if (running.get()) Logger.w(TAG, "accept failed: ${e.message}")
                            break
                        }
                        scope.launch { handle(client, sessions.incrementAndGet()) }
                    }
                } catch (e: Exception) {
                    Logger.e(TAG, "Bridge crashed (attempt $restartAttempts)", e)
                }
                restartAttempts++
                try { serverSocket?.close() } catch (_: IOException) {}
                if (running.get() && scope.isActive && restartAttempts < 6) {
                    Logger.w(TAG, "restarting bridge in 2s…")
                    delay(2000)
                }
            }
            if (restartAttempts >= 6) {
                Logger.e(TAG, "bridge watchdog gave up after 6 restarts — sharing service restart required")
            }
            running.set(false)
        }
    }

    fun stop() {
        running.set(false)
        try { serverSocket?.close() } catch (_: IOException) {}
        scope.cancel()
    }

    // ------------------------------------------------------------------ //

    private class Session {
        var authenticated = false
        var signalEnabled = false
        var signalIntervalMs = 1000
        var lastServerNonce = ""
    }

    private fun handle(socket: Socket, sessionId: Long) {
        Logger.i(TAG, "session #$sessionId from ${socket.inetAddress?.hostAddress}")
        val session = Session()
        try {
            socket.tcpNoDelay = true
            socket.keepAlive = true
            socket.soTimeout = 0
            val reader = BufferedReader(InputStreamReader(socket.getInputStream(), Charsets.UTF_8))
            val writer = OutputStreamWriter(socket.getOutputStream(), Charsets.UTF_8)

            AppState.setBridgeConnected(true)

            while (running.get()) {
                val line = try {
                    reader.readLine() ?: break
                } catch (e: IOException) {
                    break
                }
                if (line.isBlank()) continue
                val response = dispatch(session, line.trim())
                if (response != null) {
                    writer.write(response)
                    writer.write("\n")
                    writer.flush()
                }
            }
        } catch (e: Exception) {
            Logger.d(TAG, "session #$sessionId error: ${e.message}")
        } finally {
            AppState.setBridgeConnected(false)
            Logger.i(TAG, "session #$sessionId closed")
            try { socket.close() } catch (_: IOException) {}
        }
    }

    /** Returns the JSON line to send, or null for silent (event-only). */
    private fun dispatch(session: Session, line: String): String? = try {
        val req = JSONObject(line)
        val id = req.optInt("id", 0)
        val cmd = req.optString("cmd")
        val args = req.optJSONObject("args") ?: JSONObject()

        val preAuth = cmd == "auth" || cmd == "auth.verify" || cmd == "pair.token"
        if (!preAuth && !session.authenticated) {
            JSONObject().put("id", id).put("ok", false).put("error", "auth required").toString()
        } else when (cmd) {
            // hands the pairing token to the physically-connected companion.
            // Only answered while the adb broadcast opened the pairing window.
            "pair.token" -> {
                if (!PairingWindow.isOpen()) {
                    JSONObject().put("id", id).put("ok", false)
                        .put("error", "pairing window closed").toString()
                } else {
                    PairingWindow.open() // consume: one fetch per window
                    JSONObject().put("id", id).put("ok", true)
                        .put("result", JSONObject().put("token", Prefs.token))
                        .toString()
                }
            }

            "auth" -> {
                val serverNonce = UUID.randomUUID().toString().replace("-", "")
                session.lastServerNonce = serverNonce
                JSONObject()
                    .put("id", id).put("ok", true)
                    .put("result", JSONObject().put("nonce", serverNonce))
                    .toString()
            }

            "auth.verify" -> {
                val proof = args.optString("proof")
                val nonce = args.optString("nonce")
                session.authenticated = TokenAuth.verifyProof(Prefs.token, nonce, session.lastServerNonce, proof)
                if (session.authenticated) {
                    Logger.i(TAG, "client authenticated")
                }
                JSONObject()
                    .put("id", id)
                    .put("ok", session.authenticated)
                    .put(if (session.authenticated) "result" else "error",
                        if (session.authenticated) deviceInfoJson() else "bad proof")
                    .toString()
            }

            "ping" -> JSONObject().put("id", id).put("ok", true)
                .put("result", JSONObject().put("pong", System.currentTimeMillis())).toString()

            "device.info" -> JSONObject().put("id", id).put("ok", true).put("result",
                deviceInfoJson()
                    .put("batteryLevel", PhoneNet.batteryLevel(context))
                    .put("batteryTempC", PhoneNet.batteryTempC(context))
                    .put("adbEnabled", PhoneNet.isAdbEnabled(context))
                    .put("writeSecureSettings", PhoneNet.hasWriteSecureSettings(context))
            ).toString()

            "net.info" -> {
                val subs = JSONArray()
                for ((subId, slot, carrier) in PhoneNet.activeSubscriptions(context)) {
                    subs.put(JSONObject()
                        .put("subId", subId)
                        .put("slot", slot)
                        .put("carrier", carrier)
                        .put("preferredMode", PhoneNet.readPreferredMode(context, slot) ?: JSONObject.NULL)
                        .put("networkType", PhoneNet.networkTypeName(context, subId))
                    )
                }
                JSONObject().put("id", id).put("ok", true).put("result",
                    JSONObject()
                        .put("subscriptions", subs)
                        .put("usbTethered", PhoneNet.isUsbTethered(context))
                        .put("dataNetworkType", PhoneNet.networkTypeName(context))
                ).toString()
            }

            "net.mode.read" -> {
                val slot = args.optInt("slot", 0)
                JSONObject().put("id", id).put("ok", true).put("result",
                    JSONObject()
                        .put("slot", slot)
                        .put("value", PhoneNet.readPreferredMode(context, slot) ?: JSONObject.NULL)
                        .put("networkType", PhoneNet.networkTypeName(context))
                ).toString()
            }

            "net.mode.write" -> {
                val slot = args.optInt("slot", 0)
                val value = args.optInt("value", -1)
                val ok = value >= 0 && PhoneNet.writePreferredMode(context, slot, value)
                JSONObject().put("id", id).put("ok", ok)
                    .put(if (ok) "result" else "error",
                        if (ok) JSONObject().put("slot", slot).put("value", value)
                        else "write failed (WRITE_SECURE_SETTINGS granted?)")
                    .toString()
            }

            "net.type" -> {
                val sample = signalMonitor.sampleNow()
                JSONObject().put("id", id).put("ok", true).put("result",
                    if (sample != null) signalToJson(sample) else JSONObject.NULL
                ).toString()
            }

            "net.signal.start" -> {
                session.signalEnabled = true
                session.signalIntervalMs = args.optInt("intervalMs", 1000).coerceIn(250, 10_000)
                JSONObject().put("id", id).put("ok", true).toString()
            }

            "net.signal.stop" -> {
                session.signalEnabled = false
                JSONObject().put("id", id).put("ok", true).toString()
            }

            "proxy.stats" -> JSONObject().put("id", id).put("ok", true).put("result",
                JSONObject()
                    .put("running", AppState.proxyStats.value.running)
                    .put("port", AppState.proxyStats.value.port)
                    .put("bindAll", AppState.proxyStats.value.bindAll)
                    .put("activeConnections", stats.activeConnections.get())
                    .put("totalConnections", stats.totalConnections.get())
                    .put("rxBytes", stats.rxBytes.get())
                    .put("txBytes", stats.txBytes.get())
                    .put("rxRateBps", stats.rxRateBps)
                    .put("txRateBps", stats.txRateBps)
            ).toString()

            "tether.status" -> JSONObject().put("id", id).put("ok", true).put("result",
                JSONObject().put("usbTethered", PhoneNet.isUsbTethered(context))
            ).toString()

            "logs.recent" -> {
                val limit = args.optInt("limit", 200).coerceIn(10, 1000)
                val arr = JSONArray()
                for (l in Logger.recent(limit)) arr.put(l)
                JSONObject().put("id", id).put("ok", true).put("result", arr).toString()
            }

            else -> JSONObject().put("id", id).put("ok", false)
                .put("error", "unknown command: $cmd").toString()
        }
    } catch (e: Exception) {
        JSONObject().put("id", -1).put("ok", false).put("error", "bad request: ${e.message}").toString()
    }

    private fun signalToJson(s: pro.netbridge.android.core.SignalSample): JSONObject =
        JSONObject()
            .put("timestampMs", s.timestampMs)
            .put("networkType", s.networkType)
            .put("nrState", s.nrState)
            .put("rsrp", if (s.rsrp == Int.MIN_VALUE) JSONObject.NULL else s.rsrp)
            .put("rsrq", if (s.rsrq == Int.MIN_VALUE) JSONObject.NULL else s.rsrq)
            .put("rssi", if (s.rssi == Int.MIN_VALUE) JSONObject.NULL else s.rssi)
            .put("sinr", if (s.sinr == Int.MIN_VALUE) JSONObject.NULL else s.sinr)
            .put("cqi", if (s.cqi == Int.MIN_VALUE) JSONObject.NULL else s.cqi)
            .put("band", s.band)
            .put("pci", if (s.pci == Int.MIN_VALUE) JSONObject.NULL else s.pci)
            .put("tac", s.tac)
            .put("cellId", s.cellId)
            .put("operator", s.operator)
            .put("isDataConnected", s.isDataConnected)
            .put("family", s.technologyFamily)
}
