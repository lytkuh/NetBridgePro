package pro.netbridge.android.bridge

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import pro.netbridge.android.core.Logger

/**
 * Short-lived window in which the pre-auth `pair.token` bridge command may hand
 * the pairing token to the companion. The window is opened ONLY by the adb
 * broadcast sent by the physically-connected PC (shell broadcasts), which is the
 * proof of possession — a release build cannot be read via `run-as`, so this is
 * how zero-touch token sync works without weakening the bridge.
 * Network-only attackers can NOT open the window: broadcasts are local.
 */
object PairingWindow {
    @Volatile private var untilMs: Long = 0L

    fun open(seconds: Long = 60) {
        untilMs = System.currentTimeMillis() + seconds * 1000
    }

    fun isOpen(): Boolean = System.currentTimeMillis() < untilMs
}

class PairingReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        PairingWindow.open()
        Logger.i("Pair", "pairing window opened via adb broadcast")
    }
}
