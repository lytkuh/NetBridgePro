package pro.netbridge.android.core

import android.content.Context
import android.util.Log
import java.io.File
import java.text.SimpleDateFormat
import java.util.ArrayDeque
import java.util.Date
import java.util.Locale

/**
 * In-memory ring logger shared with the UI and streamed to the companion
 * through the bridge ("logs.recent").
 *
 * Every entry is ALSO appended to an app-external log file
 * (`Android/data/pro.netbridge.android/files/netbridge.log`). That location is
 * readable by `adb shell cat` WITHOUT run-as, so release builds remain fully
 * diagnosable from the companion — the PC pulls this file when a tunnel check
 * fails.
 */
object Logger {

    const val DEBUG = 0
    const val INFO = 1
    const val WARN = 2
    const val ERROR = 3

    private const val CAPACITY = 1000
    private const val FILE_MAX_BYTES = 512 * 1024
    private val lock = Any()
    private val ring = ArrayDeque<Entry>(CAPACITY)
    private val fmt = SimpleDateFormat("MM-dd HH:mm:ss.SSS", Locale.US)
    private var logFile: File? = null

    data class Entry(val time: Long, val level: Int, val tag: String, val msg: String) {
        fun render(): String {
            val lv = when (level) {
                DEBUG -> "D"; INFO -> "I"; WARN -> "W"; else -> "E"
            }
            return "${fmt.format(Date(time))} $lv/$tag: $msg"
        }
    }

    /** Call once from the Application/Service to enable file logging. */
    fun initFileLogging(context: Context) {
        try {
            val dir = context.getExternalFilesDir(null) ?: return
            logFile = File(dir, "netbridge.log")
        } catch (_: Exception) {
            logFile = null
        }
    }

    fun d(tag: String, msg: String) = log(DEBUG, tag, msg)
    fun i(tag: String, msg: String) = log(INFO, tag, msg)
    fun w(tag: String, msg: String) = log(WARN, tag, msg)
    fun e(tag: String, msg: String, tr: Throwable? = null) =
        log(ERROR, tag, if (tr != null) "$msg :: ${tr.javaClass.simpleName}: ${tr.message}" else msg)

    private fun log(level: Int, tag: String, msg: String) {
        val entry = Entry(System.currentTimeMillis(), level, tag, msg)
        synchronized(lock) {
            if (ring.size >= CAPACITY) ring.removeFirst()
            ring.addLast(entry)
            appendToFile(entry.render())
        }
        when (level) {
            DEBUG -> Log.d(tag, msg)
            INFO -> Log.i(tag, msg)
            WARN -> Log.w(tag, msg)
            else -> Log.e(tag, msg)
        }
    }

    private fun appendToFile(line: String) {
        val f = logFile ?: return
        try {
            if (f.length() > FILE_MAX_BYTES) {
                // simple rotate: keep the newest half
                val lines = f.readLines()
                f.writeText(lines.takeLast(lines.size / 2).joinToString("\n") + "\n")
            }
            f.appendText(line + "\n")
        } catch (_: Exception) {
            // file logging must never break the app
        }
    }

    fun recent(limit: Int = 200): List<String> = synchronized(lock) {
        val list: List<Entry> = ring.toList()
        val tail: List<Entry> = if (list.size > limit) list.subList(list.size - limit, list.size) else list
        tail.map { e: Entry -> e.render() }
    }

    fun clear() = synchronized(lock) { ring.clear() }
}
