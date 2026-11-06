package pro.netbridge.android.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.lifecycle.LifecycleService
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import pro.netbridge.android.core.AppState
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Prefs
import pro.netbridge.android.bridge.BridgeServer
import pro.netbridge.android.net.PhoneNet
import pro.netbridge.android.net.SignalMonitor
import pro.netbridge.android.proxy.MultiProxyServer
import pro.netbridge.android.proxy.ProxyStats
import pro.netbridge.android.ui.MainActivity
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Foreground service hosting:
 *   1. MultiProxyServer  — data plane (SOCKS5/HTTP on Prefs.proxyPort)
 *   2. BridgeServer      — control plane (JSON bridge on Prefs.bridgePort)
 *   3. SignalMonitor     — radio telemetry pipeline
 *   4. Tether watcher    — keeps AppState in sync with ncm/rndis state
 */
class ProxyService : LifecycleService() {

    private lateinit var proxyStats: ProxyStats
    private lateinit var proxy: MultiProxyServer
    private lateinit var bridge: BridgeServer
    private lateinit var signal: SignalMonitor
    private val started = AtomicBoolean(false)

    override fun onCreate() {
        super.onCreate()
        Prefs.init(applicationContext)
        proxyStats = ProxyStats()
        signal = SignalMonitor(applicationContext)
        proxy = MultiProxyServer(proxyStats)
        bridge = BridgeServer(applicationContext, proxyStats, signal)
        createChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        super.onStartCommand(intent, flags, startId)
        when (intent?.action) {
            ACTION_STOP -> {
                stopEverything()
                stopSelf()
                return START_NOT_STICKY
            }
            else -> {
                startForegroundCompat()
                if (started.compareAndSet(false, true)) {
                    startEverything()
                }
                return START_STICKY
            }
        }
    }

    override fun onBind(intent: Intent): IBinder? = super.onBind(intent)

    override fun onDestroy() {
        stopEverything()
        super.onDestroy()
    }

    private fun startEverything() {
        Logger.i(TAG, "Starting NetBridge engine")
        proxy.start()
        bridge.start()
        signal.start(Prefs.signalIntervalMs)
        AppState.setProxyStats(
            pro.netbridge.android.core.ProxyStatsSnapshot(
                running = true,
                port = Prefs.proxyPort,
                bindAll = Prefs.bindAll
            )
        )
        updateNotification()

        // Tether + stats loop
        lifecycleScope.launch {
            var lastVersion = -1L
            while (isActive) {
                delay(1000)
                proxyStats.tick()
                AppState.setProxyStats(
                    pro.netbridge.android.core.ProxyStatsSnapshot(
                        running = true,
                        port = Prefs.proxyPort,
                        bindAll = Prefs.bindAll,
                        activeConnections = proxyStats.activeConnections.get().toInt(),
                        totalConnections = proxyStats.totalConnections.get(),
                        rxBytes = proxyStats.rxBytes.get(),
                        txBytes = proxyStats.txBytes.get(),
                        rxRateBps = proxyStats.rxRateBps,
                        txRateBps = proxyStats.txRateBps
                    )
                )
                val tethered = try { PhoneNet.isUsbTethered(applicationContext) } catch (_: Exception) { false }
                AppState.setTetherActive(tethered)
                // Push signal snapshots to interested bridge sessions automatically
                val v = AppState.signalVersion.value
                if (v != lastVersion) {
                    lastVersion = v
                    updateNotificationText()
                }
            }
        }
    }

    private fun stopEverything() {
        if (!started.getAndSet(false)) return
        Logger.i(TAG, "Stopping NetBridge engine")
        try { proxy.stop() } catch (_: Exception) {}
        try { bridge.stop() } catch (_: Exception) {}
        try { signal.stop() } catch (_: Exception) {}
        AppState.setServiceRunning(false)
        AppState.setProxyStats(pro.netbridge.android.core.ProxyStatsSnapshot())
    }

    // ---- Notification ---------------------------------------------------- //

    private fun createChannel() {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val channel = NotificationChannel(
            CHANNEL_ID, "NetBridge", NotificationManager.IMPORTANCE_LOW
        )
        channel.description = "Tethering engine status"
        nm.createNotificationChannel(channel)
    }

    private fun startForegroundCompat() {
        val notification = buildNotification(baseStatusText())
        startForeground(NOTIF_ID, notification)
        if (Build.VERSION.SDK_INT >= 29) {
            try {
                startForeground(
                    NOTIF_ID, notification,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
                )
            } catch (_: Exception) {}
        }
        AppState.setServiceRunning(true)
    }

    private fun updateNotification() = updateNotificationText()

    private fun updateNotificationText() {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.notify(NOTIF_ID, buildNotification(currentStatusText()))
    }

    private fun baseStatusText(): String = getString(pro.netbridge.android.R.string.status_running)

    private fun currentStatusText(): String {
        val s = AppState.proxyStats.value
        val tether = if (AppState.tetherActive.value) "USB ✓" else "USB ✗"
        val conns = s.activeConnections
        val mbps = "%.1f".format(s.txRateBps * 8.0 / 1_000_000.0)
        return "$tether  •  $conns conn  •  ${mbps} Mbps"
    }

    private fun buildNotification(text: String): Notification {
        val openIntent = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val stopIntent = PendingIntent.getService(
            this, 1,
            Intent(this, ProxyService::class.java).setAction(ACTION_STOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(pro.netbridge.android.R.drawable.ic_notif)
            .setContentTitle("NetBridge Pro")
            .setContentText(text)
            .setOngoing(true)
            .setContentIntent(openIntent)
            .addAction(0, getString(pro.netbridge.android.R.string.stop_service), stopIntent)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .build()
    }

    companion object {
        private const val TAG = "ProxyService"
        private const val CHANNEL_ID = "netbridge"
        private const val NOTIF_ID = 1
        const val ACTION_STOP = "pro.netbridge.android.STOP"

        fun start(context: Context) {
            context.startForegroundService(Intent(context, ProxyService::class.java))
        }

        fun stop(context: Context) {
            context.startService(
                Intent(context, ProxyService::class.java).setAction(ACTION_STOP)
            )
        }
    }
}
