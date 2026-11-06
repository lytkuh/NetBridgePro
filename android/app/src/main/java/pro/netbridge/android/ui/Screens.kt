package pro.netbridge.android.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import pro.netbridge.android.R
import pro.netbridge.android.core.AppState
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Prefs
import pro.netbridge.android.net.NetworkTypes
import pro.netbridge.android.net.PhoneNet

// ---------------------------------------------------------------- //
// Network (RAN mode control)
// ---------------------------------------------------------------- //

private data class ModeEntry(val key: String, val labelRes: Int)

private val MODES = listOf(
    ModeEntry("5G_AUTO", R.string.mode_auto_5g),
    ModeEntry("5G_4G", R.string.mode_nr_lte),
    ModeEntry("5G_ONLY", R.string.mode_nr_only),
    ModeEntry("4G_AUTO", R.string.mode_auto_4g),
    ModeEntry("4G_ONLY", R.string.mode_lte_only),
    ModeEntry("3G_ONLY", R.string.mode_3g_only),
    ModeEntry("3G_2G", R.string.mode_3g_2g),
    ModeEntry("2G_ONLY", R.string.mode_2g_only)
)

@Composable
fun NetworkScreen() {
    val context = LocalContext.current
    val signal by AppState.signal.collectAsState()
    val subs = remember { PhoneNet.activeSubscriptions(context) }
    var slot by remember { mutableStateOf(subs.firstOrNull()?.second ?: 0) }
    var notice by remember { mutableStateOf(false) }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(vertical = 8.dp)
    ) {
        Card2(stringResource(R.string.network_title)) {
            Text(
                stringResource(R.string.network_hint),
                fontSize = 12.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(10.dp))

            // SIM selector (when multi-SIM)
            if (subs.size > 1) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    subs.forEach { (subId, s, carrier) ->
                        FilterChip(
                            selected = slot == s,
                            onClick = { slot = s },
                            label = { Text(stringResource(R.string.sim_number, s + 1)) }
                        )
                    }
                }
                Spacer(Modifier.height(8.dp))
            }

            val current = PhoneNet.readPreferredMode(context, slot)
            StatLine(stringResource(R.string.current_mode), current?.toString() ?: "—")
            StatLine(stringResource(R.string.current_rat),
                (signal?.networkType ?: "UNKNOWN") +
                    if (signal?.nrState == "CONNECTED" || signal?.nrState == "NOT_RESTRICTED") " +NR" else "")

            Spacer(Modifier.height(10.dp))
            ModeGrid(slot) { notice = true }
            if (notice) {
                Spacer(Modifier.height(6.dp))
                Text(
                    stringResource(R.string.apply_notice),
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.secondary
                )
            }
        }

        Card2(stringResource(R.string.smart_switcher)) {
            Text(
                stringResource(R.string.network_hint),
                fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

@Composable
private fun ModeGrid(slot: Int, onApplied: () -> Unit) {
    val context = LocalContext.current
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        MODES.forEach { mode ->
            val candidates = NetworkTypes.candidatesFor[mode.key] ?: listOf()
            val first = candidates.firstOrNull() ?: return@forEach
            Button(
                onClick = {
                    val ok = PhoneNet.writePreferredMode(context, slot, first)
                    Logger.i("Network", "mode ${mode.key} value=$first slot=$slot ok=$ok")
                    if (ok) onApplied()
                },
                modifier = Modifier.fillMaxWidth()
            ) {
                Text(stringResource(mode.labelRes))
            }
        }
    }
}

// ---------------------------------------------------------------- //
// Security
// ---------------------------------------------------------------- //

@Composable
fun SecurityScreen() {
    val context = LocalContext.current
    var token by remember { mutableStateOf(Prefs.token) }
    val wss = remember { PhoneNet.hasWriteSecureSettings(context) }
    var adbOn by remember { mutableStateOf(PhoneNet.isAdbEnabled(context)) }
    var confirmLockdown by remember { mutableStateOf(false) }

    if (confirmLockdown) {
        AlertDialog(
            onDismissRequest = { confirmLockdown = false },
            title = { Text(stringResource(R.string.lockdown_card)) },
            text = { Text(stringResource(R.string.lockdown_hint)) },
            confirmButton = {
                TextButton(onClick = {
                    adbOn = !PhoneNet.setAdbEnabled(context, false)
                    confirmLockdown = false
                }) { Text(stringResource(R.string.lockdown_on)) }
            },
            dismissButton = {
                TextButton(onClick = { confirmLockdown = false }) {
                    Text(stringResource(android.R.string.cancel))
                }
            }
        )
    }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(vertical = 8.dp)
    ) {
        Card2(stringResource(R.string.token_card)) {
            val clipboard = LocalClipboardManager.current
            Text(
                token,
                fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                fontSize = 13.sp,
                color = MaterialTheme.colorScheme.primary
            )
            Spacer(Modifier.height(6.dp))
            Text(stringResource(R.string.token_hint), fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = {
                    clipboard?.setText(AnnotatedString(token))
                    Logger.i("Security", "token copied to clipboard")
                }) { Text(stringResource(R.string.copy)) }
                Button(onClick = {
                    token = Prefs.regenerateToken()
                    Logger.i("Security", "token regenerated — press 'Sync from phone' on the companion")
                }) { Text(stringResource(R.string.regenerate)) }
            }
        }

        Card2(stringResource(R.string.grant_card)) {
            StatLine(stringResource(R.string.wss_perm), if (wss) "✓ " + stringResource(R.string.granted) else "✗ " + stringResource(R.string.not_granted))
            StatLine("adb_enabled", if (adbOn) "ON" else "OFF")
        }

        Card2(stringResource(R.string.lockdown_card)) {
            Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    if (!adbOn) stringResource(R.string.lockdown_on) else stringResource(R.string.lockdown_off),
                    fontSize = 13.sp,
                    modifier = Modifier.weight(1f)
                )
                Switch(
                    checked = !adbOn,
                    onCheckedChange = { wantLock ->
                        if (wantLock) {
                            confirmLockdown = true
                        } else {
                            adbOn = PhoneNet.setAdbEnabled(context, true)
                        }
                    }
                )
            }
            Spacer(Modifier.height(6.dp))
            Text(stringResource(R.string.lockdown_hint), fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

// ---------------------------------------------------------------- //
// Settings
// ---------------------------------------------------------------- //

@Composable
fun SettingsScreen() {
    val context = LocalContext.current
    var proxyPort by rememberSaveable { mutableStateOf(Prefs.proxyPort.toString()) }
    var bridgePort by rememberSaveable { mutableStateOf(Prefs.bridgePort.toString()) }
    var bindAll by rememberSaveable { mutableStateOf(Prefs.bindAll) }
    var requireAuth by rememberSaveable { mutableStateOf(Prefs.requireAuth) }
    var interval by rememberSaveable { mutableStateOf(Prefs.signalIntervalMs.toString()) }
    var startOnBoot by rememberSaveable { mutableStateOf(Prefs.startOnBoot) }
    var autoStartService by rememberSaveable { mutableStateOf(Prefs.autoStartService) }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(vertical = 8.dp)
    ) {
        Card2(stringResource(R.string.settings_title)) {
            OutlinedTextField(
                value = proxyPort,
                onValueChange = { proxyPort = it.filter { c -> c.isDigit() }.take(5) },
                label = { Text(stringResource(R.string.proxy_port)) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(6.dp))
            OutlinedTextField(
                value = bridgePort,
                onValueChange = { bridgePort = it.filter { c -> c.isDigit() }.take(5) },
                label = { Text(stringResource(R.string.bridge_port)) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(6.dp))
            OutlinedTextField(
                value = interval,
                onValueChange = { interval = it.filter { c -> c.isDigit() }.take(5) },
                label = { Text(stringResource(R.string.signal_interval)) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(10.dp))
            ToggleRow(stringResource(R.string.bind_all), bindAll) {
                bindAll = it; Prefs.bindAll = it
            }
            ToggleRow(stringResource(R.string.require_auth), requireAuth) {
                requireAuth = it; Prefs.requireAuth = it
            }
            ToggleRow(stringResource(R.string.start_on_boot), startOnBoot) {
                startOnBoot = it; Prefs.startOnBoot = it
            }
            ToggleRow(stringResource(R.string.auto_start_service), autoStartService) {
                autoStartService = it; Prefs.autoStartService = it
            }
            Spacer(Modifier.height(10.dp))
            Button(onClick = {
                val pp = proxyPort.toIntOrNull() ?: Prefs.DEFAULT_PROXY_PORT
                val bp = bridgePort.toIntOrNull() ?: Prefs.DEFAULT_BRIDGE_PORT
                Prefs.proxyPort = pp.coerceIn(1024, 65000)
                Prefs.bridgePort = bp.coerceIn(1024, 65000)
                Prefs.signalIntervalMs = (interval.toIntOrNull() ?: 1000).coerceIn(250, 10_000)
                Logger.i("Settings", "ports saved: proxy=$pp bridge=$bp")
            }) { Text("💾  " + stringResource(R.string.save)) }
        }

        Card2(stringResource(R.string.language)) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(
                    selected = Prefs.language == "fa",
                    onClick = {
                        Prefs.language = "fa"
                        (context as? pro.netbridge.android.ui.MainActivity)?.recreate()
                    },
                    label = { Text("فارسی") }
                )
                FilterChip(
                    selected = Prefs.language == "en",
                    onClick = {
                        Prefs.language = "en"
                        (context as? pro.netbridge.android.ui.MainActivity)?.recreate()
                    },
                    label = { Text("English") }
                )
            }
        }

    }
}

@Composable
private fun ToggleRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(
        Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, fontSize = 13.sp, modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange)
    }
}
