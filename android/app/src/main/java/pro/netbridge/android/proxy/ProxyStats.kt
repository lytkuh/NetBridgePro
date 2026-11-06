package pro.netbridge.android.proxy

import java.util.concurrent.atomic.AtomicLong

/**
 * Traffic counters for the proxy engine. Rates are computed with a 1-second
 * exponential moving average to keep the UI smooth without a big buffer.
 */
class ProxyStats {

    val rxBytes = AtomicLong(0)
    val txBytes = AtomicLong(0)
    val activeConnections = AtomicLong(0)
    val totalConnections = AtomicLong(0)

    @Volatile var rxRateBps: Long = 0
        private set
    @Volatile var txRateBps: Long = 0
        private set

    private var lastRx = 0L
    private var lastTx = 0L
    private var lastTick = 0L

    fun addRx(n: Int) { rxBytes.addAndGet(n.toLong()) }
    fun addTx(n: Int) { txBytes.addAndGet(n.toLong()) }

    fun connectionOpened() {
        activeConnections.incrementAndGet()
        totalConnections.incrementAndGet()
    }

    fun connectionClosed() { activeConnections.decrementAndGet() }

    /** Called every second by the owning service. */
    fun tick() {
        val now = System.currentTimeMillis()
        synchronized(this) {
            if (lastTick == 0L) {
                lastTick = now
                lastRx = rxBytes.get()
                lastTx = txBytes.get()
                return
            }
            val dt = (now - lastTick).coerceAtLeast(1) / 1000.0
            val rx = rxBytes.get()
            val tx = txBytes.get()
            val rxRate = ((rx - lastRx) / dt).toLong().coerceAtLeast(0)
            val txRate = ((tx - lastTx) / dt).toLong().coerceAtLeast(0)
            // EMA, alpha = 0.5
            rxRateBps = (rxRateBps * 0.5 + rxRate * 0.5).toLong()
            txRateBps = (txRateBps * 0.5 + txRate * 0.5).toLong()
            lastRx = rx
            lastTx = tx
            lastTick = now
        }
    }
}
