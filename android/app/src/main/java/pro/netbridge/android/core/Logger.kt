package pro.netbridge.android.core

import android.util.Log
import java.text.SimpleDateFormat
import java.util.ArrayDeque
import java.util.Date
import java.util.Locale

/**
 * In-memory ring logger shared with the UI and streamed to the companion
 * through the bridge ("logs.recent").
 */
object Logger {

    const val DEBUG = 0
    const val INFO = 1
    const val WARN = 2
    const val ERROR = 3

    private const val CAPACITY = 1000
    private val lock = Any()
    private val ring = ArrayDeque<Entry>(CAPACITY)
    private val fmt = SimpleDateFormat("MM-dd HH:mm:ss.SSS", Locale.US)

    data class Entry(val time: Long, val level: Int, val tag: String, val msg: String) {
        fun render(): String {
            val lv = when (level) {
                DEBUG -> "D"; INFO -> "I"; WARN -> "W"; else -> "E"
            }
            return "${fmt.format(Date(time))} $lv/$tag: $msg"
        }
    }

    fun d(tag: String, msg: String) = log(DEBUG, tag, msg)
    fun i(tag: String, msg: String) = log(INFO, tag, msg)
    fun w(tag: String, msg: String) = log(WARN, tag, msg)
    fun e(tag: String, msg: String, tr: Throwable? = null) =
        log(ERROR, tag, if (tr != null) "$msg :: ${tr.javaClass.simpleName}: ${tr.message}" else msg)

    private fun log(level: Int, tag: String, msg: String) {
        synchronized(lock) {
            if (ring.size >= CAPACITY) ring.removeFirst()
            ring.addLast(Entry(System.currentTimeMillis(), level, tag, msg))
        }
        when (level) {
            DEBUG -> Log.d(tag, msg)
            INFO -> Log.i(tag, msg)
            WARN -> Log.w(tag, msg)
            else -> Log.e(tag, msg)
        }
    }

    fun recent(limit: Int = 200): List<String> = synchronized(lock) {
        ring.toList().takeLast(limit).map { it.render() }
    }

    fun clear() = synchronized(lock) { ring.clear() }
}
