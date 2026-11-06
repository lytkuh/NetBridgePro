package pro.netbridge.android

import android.app.Application
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Prefs

class App : Application() {

    override fun onCreate() {
        super.onCreate()
        Prefs.init(this)
        Logger.i(TAG, "NetBridge Pro started (v${BuildConfig.VERSION_NAME})")
    }

    companion object {
        private const val TAG = "App"
    }
}
