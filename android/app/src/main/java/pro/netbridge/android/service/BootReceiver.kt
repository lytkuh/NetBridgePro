package pro.netbridge.android.service

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Prefs

/** Starts the sharing engine after boot when the user opted in. */
class BootReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED) return
        Prefs.init(context)
        if (!Prefs.startOnBoot) return
        Logger.i("Boot", "startOnBoot is enabled -> starting service")
        if (Build.VERSION.SDK_INT >= 26) {
            context.startForegroundService(Intent(context, ProxyService::class.java))
        } else {
            context.startService(Intent(context, ProxyService::class.java))
        }
    }
}
