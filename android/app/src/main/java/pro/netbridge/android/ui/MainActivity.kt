package pro.netbridge.android.ui

import android.content.Context
import android.content.res.Configuration
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Scaffold
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Dashboard
import androidx.compose.material.icons.filled.CellTower
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Security
import androidx.compose.material.icons.filled.Settings
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.util.Locale
import kotlinx.coroutines.delay
import pro.netbridge.android.core.AppState
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Prefs
import pro.netbridge.android.core.SignalSample
import pro.netbridge.android.service.ProxyService

class MainActivity : ComponentActivity() {

    private val notifPermission =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) {}

    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(wrap(newBase))
    }

    private fun wrap(base: Context): Context {
        Prefs.init(base.applicationContext)
        val lang = Prefs.language
        val loc = Locale(lang)
        Locale.setDefault(loc)
        val cfg = Configuration(base.resources.configuration)
        cfg.setLocale(loc)
        return base.createConfigurationContext(cfg)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= 33) {
            notifPermission.launch(android.Manifest.permission.POST_NOTIFICATIONS)
        }
        // zero-touch: bring the sharing service up the moment the app opens
        if (Prefs.autoStartService && !AppState.serviceRunning.value) {
            try {
                ProxyService.start(this)
            } catch (e: Exception) {
                Logger.e("Main", "auto start service failed: ${e.message}")
            }
        }
        setContent {
            NetBridgeTheme(dark = true) {
                // Persian is RTL-first: pin the layout direction explicitly instead of
                // relying on the wrapped-configuration propagation on every OEM skin.
                val dir = if (Prefs.language == "fa") LayoutDirection.Rtl else LayoutDirection.Ltr
                CompositionLocalProvider(LocalLayoutDirection provides dir) {
                    Root()
                }
            }
        }
    }
}

private enum class Tab { Dashboard, Network, Security, Settings, About }

@Composable
fun Root() {
    var tab by remember { mutableIntStateOf(0) }
    val titles = listOf(
        androidx.compose.ui.res.stringResource(pro.netbridge.android.R.string.tab_dashboard),
        androidx.compose.ui.res.stringResource(pro.netbridge.android.R.string.tab_network),
        androidx.compose.ui.res.stringResource(pro.netbridge.android.R.string.tab_security),
        androidx.compose.ui.res.stringResource(pro.netbridge.android.R.string.tab_settings),
        androidx.compose.ui.res.stringResource(pro.netbridge.android.R.string.tab_about)
    )
    val icons = listOf(
        Icons.Filled.Dashboard, Icons.Filled.CellTower,
        Icons.Filled.Security, Icons.Filled.Settings,
        Icons.Filled.Info
    )

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        bottomBar = {
            NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                for (i in titles.indices) {
                    NavigationBarItem(
                        selected = tab == i,
                        onClick = { tab = i },
                        icon = { Icon(icons[i], contentDescription = null) },
                        label = { Text(titles[i], fontSize = 11.sp) }
                    )
                }
            }
        }
    ) { padding ->
        Box(Modifier.padding(padding)) {
            when (tab) {
                0 -> DashboardScreen()
                1 -> NetworkScreen()
                2 -> SecurityScreen()
                3 -> SettingsScreen()
                else -> AboutScreen()
            }
        }
    }
}

// ---------------------------------------------------------------- //
// Shared composables
// ---------------------------------------------------------------- //

@Composable
fun Card2(title: String, content: @Composable () -> Unit) {
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 6.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        shape = RoundedCornerShape(14.dp)
    ) {
        Column(Modifier.padding(14.dp)) {
            Text(
                title,
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.primary
            )
            Spacer(Modifier.height(8.dp))
            content()
        }
    }
}

@Composable
fun StatLine(label: String, value: String, mono: Boolean = true) {
    Row(
        Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp)
        Text(
            value,
            color = MaterialTheme.colorScheme.onSurface,
            fontSize = 13.sp,
            fontFamily = if (mono) FontFamily.Monospace else null
        )
    }
}

private fun fmtBytes(b: Long): String = when {
    b >= 1L shl 30 -> "%.2f GB".format(b / 1073741824.0)
    b >= 1L shl 20 -> "%.1f MB".format(b / 1048576.0)
    b >= 1L shl 10 -> "%.1f KB".format(b / 1024.0)
    else -> "$b B"
}

private fun fmtRate(bps: Long): String = when {
    bps >= 1_000_000 -> "%.1f Mbps".format(bps * 8.0 / 1_000_000.0)
    bps >= 1_000 -> "%.0f Kbps".format(bps * 8.0 / 1_000.0)
    else -> "${bps * 8} bps"
}

// ---------------------------------------------------------------- //
// Dashboard
// ---------------------------------------------------------------- //

@Composable
fun DashboardScreen() {
    val context = LocalContext.current
    val stats by AppState.proxyStats.collectAsState()
    val signal by AppState.signal.collectAsState()
    val tether by AppState.tetherActive.collectAsState()
    val bridge by AppState.bridgeConnected.collectAsState()
    val running by AppState.serviceRunning.collectAsState()

    // Keep state fresh even if service counters stall
    LaunchedEffect(Unit) { while (true) { delay(1000) } }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(vertical = 8.dp)
    ) {
        // Big control
        Card(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 6.dp),
            colors = CardDefaults.cardColors(
                containerColor = if (running) MaterialTheme.colorScheme.surface
                else MaterialTheme.colorScheme.surfaceVariant
            ),
            shape = RoundedCornerShape(16.dp)
        ) {
            Column(Modifier.padding(18.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    if (running) stringResource(pro.netbridge.android.R.string.status_running)
                    else stringResource(pro.netbridge.android.R.string.status_idle),
                    fontSize = 13.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Spacer(Modifier.height(8.dp))
                Button(
                    onClick = {
                        if (running) ProxyService.stop(context) else ProxyService.start(context)
                    },
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text(
                        if (running)
                            androidx.compose.ui.res.stringResource(pro.netbridge.android.R.string.stop_service)
                        else
                            androidx.compose.ui.res.stringResource(pro.netbridge.android.R.string.start_service)
                    )
                }
            }
        }

        Card2(stringResource(pro.netbridge.android.R.string.proxy_card)) {
            StatLine(stringResource(pro.netbridge.android.R.string.proxy_clients), "${stats.activeConnections}")
            StatLine(stringResource(pro.netbridge.android.R.string.proxy_down), "${fmtBytes(stats.rxBytes)}  (${fmtRate(stats.txRateBps)})")
            StatLine(stringResource(pro.netbridge.android.R.string.proxy_up), "${fmtBytes(stats.txBytes)}  (${fmtRate(stats.rxRateBps)})")
            StatLine("Port", "${stats.port}" + if (stats.bindAll) " (0.0.0.0)" else " (127.0.0.1)")
        }

        Card2(stringResource(pro.netbridge.android.R.string.tether_card)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    Modifier
                        .size(10.dp)
                        .background(
                            if (tether) MaterialTheme.colorScheme.secondary
                            else MaterialTheme.colorScheme.error,
                            RoundedCornerShape(5.dp)
                        )
                )
                Spacer(Modifier.width(8.dp))
                Text(
                    if (tether) stringResource(pro.netbridge.android.R.string.tether_on)
                    else stringResource(pro.netbridge.android.R.string.tether_off),
                    fontSize = 13.sp
                )
            }
        }

        Card2(stringResource(pro.netbridge.android.R.string.signal_card)) {
            if (signal == null) {
                Text(
                    stringResource(pro.netbridge.android.R.string.no_signal),
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    fontSize = 13.sp
                )
            } else {
                SignalSummary(signal!!)
            }
        }

        Card2(stringResource(pro.netbridge.android.R.string.bridge_card)) {
            Text(
                if (bridge) stringResource(pro.netbridge.android.R.string.bridge_connected)
                else stringResource(pro.netbridge.android.R.string.bridge_disconnected),
                fontSize = 13.sp,
                color = if (bridge) MaterialTheme.colorScheme.secondary
                else MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

@Composable
fun SignalSummary(s: SignalSample) {
    Text(
        text = "${s.networkType}" + if (s.nrState == "CONNECTED" || s.nrState == "NOT_RESTRICTED") " +NR" else "",
        style = MaterialTheme.typography.headlineSmall,
        color = MaterialTheme.colorScheme.primary
    )
    Spacer(Modifier.height(4.dp))
    if (s.rsrp != Int.MIN_VALUE) StatLine("RSRP", "${s.rsrp} dBm")
    if (s.rsrq != Int.MIN_VALUE) StatLine("RSRQ", "${s.rsrq} dB")
    if (s.sinr != Int.MIN_VALUE) StatLine("SINR", "${s.sinr} dB")
    if (s.rssi != Int.MIN_VALUE) StatLine("RSSI", "${s.rssi} dBm")
    if (s.band.isNotEmpty()) StatLine("Band", s.band)
    if (s.operator.isNotEmpty()) StatLine("Operator", s.operator, mono = false)
}

// ---------------------------------------------------------------- //
// Network screen (implemented in Screens.kt)
// ---------------------------------------------------------------- //
