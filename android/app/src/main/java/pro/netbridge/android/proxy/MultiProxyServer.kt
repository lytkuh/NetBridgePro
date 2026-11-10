package pro.netbridge.android.proxy

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.joinAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import pro.netbridge.android.core.Logger
import pro.netbridge.android.core.Prefs
import pro.netbridge.android.core.TokenAuth
import java.io.BufferedInputStream
import java.io.DataInputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketAddress
import java.net.SocketTimeoutException
import java.net.URI
import java.net.UnknownHostException
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.min

/**
 * NetBridge data plane — a single port multiplexing two proxy dialects:
 *
 *   * SOCKS5 (RFC 1928) with RFC 1929 username/password auth,
 *     CONNECT and UDP ASSOCIATE
 *   * HTTP proxy (CONNECT + absolute-form requests)
 *
 * Protocol is detected from the first byte: 0x04/0x05 => SOCKS, otherwise the
 * stream is expected to start with an HTTP request line.
 *
 * Performance: 256 KiB socket buffers, TCP_NODELAY, 64 KiB relay buffer,
 * one coroutine per direction, cooperative shutdown on close.
 */
class MultiProxyServer(private val stats: ProxyStats) {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val running = AtomicBoolean(false)
    private var serverSocket: ServerSocket? = null
    private var restartAttempts = 0

    companion object {
        private const val TAG = "Proxy"
        private const val RELAY_BUF = 64 * 1024
        private const val HEAD_LIMIT = 32 * 1024
        private const val CONNECT_TIMEOUT_MS = 12_000
        private const val SOCKS_VER5 = 0x05
        private const val SOCKS_VER4 = 0x04

        private const val REP_OK = 0x00
        private const val REP_GENERAL_FAIL = 0x01
        private const val REP_HOST_UNREACHABLE = 0x04
        private const val REP_CONN_REFUSED = 0x05
        private const val REP_TTL_EXPIRED = 0x06
        private const val REP_CMD_NOT_SUPPORTED = 0x07
        private const val REP_ATYP_NOT_SUPPORTED = 0x08
    }

    fun start() {
        restartAttempts = 0 // every explicit start gets a fresh watchdog budget
        if (!running.compareAndSet(false, true)) return
        scope.launch {
            val port = Prefs.proxyPort
            // Watchdog: a single accept/bind crash used to kill the data plane
            // for good ("connected but no internet"). Restart up to 6 times.
            while (running.get() && scope.isActive && restartAttempts < 6) {
                try {
                    val ss = ServerSocket()
                    ss.reuseAddress = true
                    ss.bind(InetSocketAddress(if (Prefs.bindAll) "0.0.0.0" else "127.0.0.1", port))
                    serverSocket = ss
                    Logger.i(TAG, "Listening on ${if (Prefs.bindAll) "0.0.0.0" else "127.0.0.1"}:$port")
                    while (running.get() && scope.isActive) {
                        val client = try {
                            ss.accept()
                        } catch (e: IOException) {
                            if (running.get()) Logger.w(TAG, "accept failed: ${e.message}")
                            break
                        }
                        scope.launch { handleClient(client) }
                    }
                } catch (e: Exception) {
                    Logger.e(TAG, "Proxy server crashed (attempt $restartAttempts)", e)
                }
                restartAttempts++
                try { serverSocket?.close() } catch (_: IOException) {}
                if (running.get() && scope.isActive && restartAttempts < 6) {
                    Logger.w(TAG, "restarting proxy server in 2s…")
                    delay(2000)
                }
            }
            if (restartAttempts >= 6) {
                Logger.e(TAG, "proxy watchdog gave up after 6 restarts — sharing service restart required")
            }
            running.set(false)
            Logger.i(TAG, "Proxy server stopped")
        }
    }

    fun stop() {
        running.set(false)
        try { serverSocket?.close() } catch (_: IOException) {}
        scope.cancel()
    }

    // ------------------------------------------------------------------ //

    private fun handleClient(socket: Socket) {
        stats.connectionOpened()
        try {
            socket.tcpNoDelay = true
            socket.keepAlive = true
            socket.receiveBufferSize = 256 * 1024
            socket.sendBufferSize = 256 * 1024
            socket.soTimeout = 30_000

            val input = BufferedInputStream(socket.getInputStream(), 8 * 1024)
            // Consume the first byte WITHOUT mark/reset. The previous
            // mark(8)/read()/reset() dance corrupted the stream state on this
            // device and dropped every byte after the method negotiation
            // ("stream ended" + RFC1929 "Read timed out" on the companion).
            val first = input.read()

            when (first) {
                SOCKS_VER5, SOCKS_VER4 -> socksHandler(socket, input, first)
                -1 -> socket.close()
                else -> httpHandler(socket, input, first)
            }
        } catch (e: Exception) {
            Logger.d(TAG, "client error: ${e.javaClass.simpleName}: ${e.message}")
            try { socket.close() } catch (_: IOException) {}
        } finally {
            stats.connectionClosed()
        }
    }

    // ============================ SOCKS =============================== //

    private fun socksHandler(socket: Socket, input: InputStream, version: Int) {
        val dis = DataInputStream(input)
        when (version) {
            SOCKS_VER5 -> socks5(socket, dis, input)
            SOCKS_VER4 -> socks4(socket, dis)
            else -> socket.close()
        }
    }

    private fun socks4(socket: Socket, dis: DataInputStream) {
        try {
            dis.readByte() // command byte (0x01 CONNECT)
            val port = dis.readUnsignedShort()
            val ip = ByteArray(4).also { dis.readFully(it) }
            while (dis.readByte().toInt() != 0) { /* userid */ }

            val dstAddr: String
            if (ip[0] == 0.toByte() && ip[1] == 0.toByte() &&
                ip[2] == 0.toByte() && ip[3] != 0.toByte()
            ) {
                // SOCKS4a domain
                val sb = StringBuilder()
                while (true) {
                    val b = dis.readByte().toInt()
                    if (b == 0) break
                    sb.append(b.toChar())
                }
                dstAddr = sb.toString()
            } else {
                dstAddr = ip.joinToString(".") { (it.toInt() and 0xFF).toString() }
            }

            val remote = Socket()
            try {
                remote.tcpNoDelay = true
                remote.connect(InetSocketAddress(dstAddr, port), CONNECT_TIMEOUT_MS)
                val reply = ByteArray(8)
                reply[1] = 0x5A
                reply[2] = (port shr 8).toByte()
                reply[3] = port.toByte()
                socket.getOutputStream().apply { write(reply); flush() }
                relay(socket, remote, toRemote = true)
            } catch (e: Exception) {
                remote.close()
                socket.getOutputStream().apply {
                    write(byteArrayOf(0, 0x5B, 0, 0, 0, 0, 0, 0)); flush()
                }
                socket.close()
            }
        } catch (e: Exception) {
            try { socket.close() } catch (_: IOException) {}
        }
    }

    private fun socks5(socket: Socket, dis: DataInputStream, input: InputStream) {
        val out = socket.getOutputStream()
        try {
            // -- method negotiation (with byte-level diagnostics)
            val nmethods = dis.readUnsignedByte()
            val methods = ByteArray(nmethods).also { dis.readFully(it) }
            Logger.d(TAG, "socks5 diag: nmethods=$nmethods methods=${methods.joinToString("") { "%02X".format(it) }}")
            val wantUserPass = Prefs.requireAuth
            val authAccepted = when {
                !wantUserPass && methods.contains(0x00.toByte()) -> 0x00
                methods.contains(0x02.toByte()) -> 0x02
                else -> -1
            }
            if (authAccepted == -1) {
                out.write(byteArrayOf(SOCKS_VER5.toByte(), 0xFF.toByte()))
                out.flush()
                socket.close()
                return
            }
            out.write(byteArrayOf(SOCKS_VER5.toByte(), authAccepted.toByte()))
            out.flush()

            // -- RFC 1929 user/pass
            if (authAccepted == 0x02) {
                Logger.d(TAG, "socks5 diag: waiting for RFC1929 credentials… available=${input.available()}")
                // RFC 1929 frame: ver(0x01) | uLen | uname | pLen | passwd
                // The version byte MUST be consumed first — skipping it shifted
                // every field by one byte and made the parser block forever
                // waiting for a pass length of 110, killing the data plane.
                val authVer = dis.readUnsignedByte()
                if (authVer != 0x01) {
                    Logger.w(TAG, "RFC1929 auth version mismatch: $authVer")
                    socket.close()
                    return
                }
                val uLen = dis.readUnsignedByte()
                val user = ByteArray(uLen).also { dis.readFully(it) }.toString(Charsets.ISO_8859_1)
                val pLen = dis.readUnsignedByte()
                val pass = ByteArray(pLen).also { dis.readFully(it) }.toString(Charsets.ISO_8859_1)
                Logger.d(TAG, "socks5 diag: uLen=$uLen pLen=$pLen passLen=${pass.length}")
                val ok = TokenAuth.verifyProxyCreds(user, pass)
                out.write(byteArrayOf(0x01, (if (ok) 0x00 else 0x01).toByte()))
                out.flush()
                if (!ok) {
                    Logger.w(TAG, "SOCKS5 auth failed for user=$user")
                    socket.close()
                    return
                }
            }

            // -- request
            val ver = dis.readUnsignedByte()
            if (ver != SOCKS_VER5) { socket.close(); return }
            val cmd = dis.readUnsignedByte()
            dis.readUnsignedByte() // RSV
            val atyp = dis.readUnsignedByte()

            val targetHost: String
            when (atyp) {
                0x01 -> {
                    val ip = ByteArray(4).also { dis.readFully(it) }
                    targetHost = ip.joinToString(".") { (it.toInt() and 0xFF).toString() }
                }
                0x03 -> {
                    val len = dis.readUnsignedByte()
                    targetHost = ByteArray(len).also { dis.readFully(it) }.toString(Charsets.UTF_8)
                }
                0x04 -> {
                    val ip = ByteArray(16).also { dis.readFully(it) }
                    targetHost = InetAddress.getByAddress(ip).hostAddress ?: "0.0.0.0"
                }
                else -> {
                    socksReply(out, REP_ATYP_NOT_SUPPORTED, "0.0.0.0", 0)
                    socket.close()
                    return
                }
            }
            val targetPort = dis.readUnsignedShort()

            when (cmd) {
                0x01 -> socksConnect(socket, out, targetHost, targetPort)
                0x03 -> udpAssociate(socket, dis, out)
                else -> {
                    socksReply(out, REP_CMD_NOT_SUPPORTED, "0.0.0.0", 0)
                    socket.close()
                }
            }
        } catch (e: Exception) {
            Logger.d(TAG, "socks5 error: ${e.message}")
            try { socket.close() } catch (_: IOException) {}
        }
    }

    private fun socksConnect(socket: Socket, out: OutputStream, host: String, port: Int) {
        val remote = Socket()
        try {
            remote.tcpNoDelay = true
            remote.keepAlive = true
            remote.receiveBufferSize = 256 * 1024
            remote.sendBufferSize = 256 * 1024
            remote.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
        } catch (e: Exception) {
            Logger.e(TAG, "outbound connect failed: $host:$port :: ${e.javaClass.simpleName}: ${e.message}")
            try { remote.close() } catch (_: IOException) {}
            val rep = when (e) {
                is SocketTimeoutException -> REP_TTL_EXPIRED
                is java.net.ConnectException -> REP_CONN_REFUSED
                is UnknownHostException -> REP_HOST_UNREACHABLE
                else -> REP_GENERAL_FAIL
            }
            try {
                socksReply(out, rep, "0.0.0.0", 0)
                socket.close()
            } catch (_: IOException) {}
            return
        }

        val localAddr = localAddressForClient(socket)
        try {
            socksReply(out, REP_OK, localAddr.first, remote.localPort)
        } catch (_: IOException) {
            try { remote.close() } catch (_: IOException) {}
            try { socket.close() } catch (_: IOException) {}
            return
        }

        socket.soTimeout = 0
        remote.soTimeout = 0
        relay(socket, remote, toRemote = true)
    }

    private class UdpParsed(val address: InetAddress, val port: Int, val headerLen: Int)

    private fun udpAssociate(socket: Socket, dis: DataInputStream, out: OutputStream) {
        var udp: DatagramSocket? = null
        try {
            udp = DatagramSocket(0)
            udp.receiveBufferSize = 256 * 1024
            udp.sendBufferSize = 256 * 1024

            val local = localAddressForClient(socket)
            socksReply(out, REP_OK, local.first, udp.localPort)
            socket.soTimeout = 0

            // Control watcher: TCP control connection dying closes the relay.
            val ctrlWatcher = scope.launch {
                try {
                    val buf = ByteArray(64)
                    while (true) {
                        val n = dis.read(buf)
                        if (n < 0) break
                    }
                } catch (_: IOException) {
                } finally {
                    try { udp?.close() } catch (_: Exception) {}
                }
            }

            val rx = ByteArray(65536)
            var lastClient: SocketAddress? = null
            while (!udp.isClosed) {
                val packet = DatagramPacket(rx, rx.size)
                try {
                    udp.receive(packet)
                } catch (_: IOException) {
                    break
                }
                val sender = packet.socketAddress
                if (lastClient == null) {
                    lastClient = sender
                }
                if (sender != lastClient) {
                    // From a target server -> encapsulate, forward to the client
                    val cli = lastClient as InetSocketAddress
                    val head = buildUdpHeader(packet.address, packet.port)
                    val out1 = ByteArray(head.size + packet.length)
                    head.copyInto(out1)
                    System.arraycopy(packet.data, packet.offset, out1, head.size, packet.length)
                    udp.send(DatagramPacket(out1, out1.size, cli.address, cli.port))
                    stats.addTx(packet.length)
                } else {
                    // From the client -> strip SOCKS header, forward to target
                    val parsed = parseUdpHeader(packet.data, packet.length) ?: continue
                    udp.send(
                        DatagramPacket(
                            packet.data, parsed.headerLen, packet.length - parsed.headerLen,
                            parsed.address, parsed.port
                        )
                    )
                    stats.addRx(packet.length)
                }
            }
            ctrlWatcher.cancel()
        } catch (e: Exception) {
            Logger.d(TAG, "udp associate failed: ${e.message}")
        } finally {
            try { udp?.close() } catch (_: Exception) {}
            try { socket.close() } catch (_: IOException) {}
        }
    }

    private fun buildUdpHeader(addr: InetAddress, port: Int): ByteArray {
        val ip = addr.address
        val atyp = if (ip.size == 4) 0x01 else 0x04
        val head = ByteArray(4 + ip.size + 2)
        head[0] = 0; head[1] = 0; head[2] = 0; head[3] = atyp.toByte()
        ip.copyInto(head, 4)
        head[4 + ip.size] = (port shr 8).toByte()
        head[5 + ip.size] = port.toByte()
        return head
    }

    private fun parseUdpHeader(data: ByteArray, len: Int): UdpParsed? {
        if (len < 4) return null
        val atyp = data[3].toInt() and 0xFF
        return when (atyp) {
            0x01 -> {
                if (len < 10) return null
                val ip = data.copyOfRange(4, 8)
                val port = ((data[8].toInt() and 0xFF) shl 8) or (data[9].toInt() and 0xFF)
                UdpParsed(InetAddress.getByAddress(ip), port, 10)
            }
            0x03 -> {
                if (len < 5) return null
                val dl = data[4].toInt() and 0xFF
                val total = 5 + dl + 2
                if (len < total) return null
                val host = data.decodeToString(5, 5 + dl)
                val port = ((data[5 + dl].toInt() and 0xFF) shl 8) or (data[6 + dl].toInt() and 0xFF)
                UdpParsed(InetAddress.getByName(host), port, total)
            }
            0x04 -> {
                if (len < 22) return null
                val ip = data.copyOfRange(4, 20)
                val port = ((data[20].toInt() and 0xFF) shl 8) or (data[21].toInt() and 0xFF)
                UdpParsed(InetAddress.getByAddress(ip), port, 22)
            }
            else -> null
        }
    }

    // ============================ HTTP ================================ //

    private fun httpHandler(socket: Socket, rawInput: InputStream, firstByte: Int) {
        try {
            if (Prefs.requireAuth && !httpCheckAuth(rawInput, firstByte)) {
                socket.getOutputStream().write(
                    ("HTTP/1.1 407 Proxy Authentication Required\r\n" +
                        "Proxy-Authenticate: Basic realm=\"NetBridge\"\r\n" +
                        "Content-Length: 0\r\n\r\n").toByteArray(Charsets.ISO_8859_1)
                )
                socket.getOutputStream().flush()
                socket.close()
                return
            }

            // firstByte was consumed during protocol detection — prepend it
            val headRest = readHttpHead(rawInput) ?: run { socket.close(); return }
            val head = (if (firstByte in 0..0xFF) firstByte.toChar().toString() else "") + headRest
            val lines = head.split("\r\n")
            val requestLine = lines.firstOrNull() ?: run { socket.close(); return }
            val parts = requestLine.trim().split(" ")
            if (parts.size < 3) { socket.close(); return }
            val method = parts[0].uppercase()
            val target = parts[1]

            if (method == "CONNECT") {
                val hp = target.split(":")
                val host = hp[0]
                val port = if (hp.size > 1) hp[1].toIntOrNull() ?: 443 else 443
                socket.getOutputStream().write("HTTP/1.1 200 Connection Established\r\n\r\n".toByteArray(Charsets.ISO_8859_1))
                socket.getOutputStream().flush()
                socket.soTimeout = 0
                val remote = Socket()
                try {
                    remote.tcpNoDelay = true
                    remote.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
                    relay(socket, remote, toRemote = true)
                } catch (e: Exception) {
                    try { remote.close() } catch (_: IOException) {}
                    try { socket.close() } catch (_: IOException) {}
                }
            } else {
                // absolute-form request (system proxy plain-HTTP path)
                val uri = URI(target)
                val host = uri.host ?: run { socket.close(); return }
                val port = if (uri.port > 0) uri.port else 80
                val remote = Socket()
                try {
                    remote.tcpNoDelay = true
                    remote.connect(InetSocketAddress(host, port), CONNECT_TIMEOUT_MS)
                    val sb = StringBuilder()
                    sb.append("${parts[0]} ${uri.rawPath ?: "/"}${uri.rawQuery?.let { "?$it" } ?: ""} HTTP/1.1\r\n")
                    for (line in lines.drop(1)) {
                        if (line.isEmpty()) continue
                        val lower = line.lowercase()
                        if (lower.startsWith("proxy-") || lower.startsWith("connection") ||
                            lower.startsWith("keep-alive") || lower.startsWith("te:")
                        ) continue
                        sb.append(line).append("\r\n")
                    }
                    sb.append("Connection: close\r\n\r\n")
                    remote.getOutputStream().write(sb.toString().toByteArray(Charsets.ISO_8859_1))
                    remote.getOutputStream().flush()
                    socket.soTimeout = 0
                    relay(socket, remote, toRemote = true)
                } catch (e: Exception) {
                    try { remote.close() } catch (_: IOException) {}
                    try { socket.close() } catch (_: IOException) {}
                }
            }
        } catch (e: Exception) {
            Logger.d(TAG, "http handler error: ${e.message}")
            try { socket.close() } catch (_: IOException) {}
        }
    }

    private fun httpCheckAuth(rawInput: InputStream, firstByte: Int): Boolean {
        rawInput.mark(HEAD_LIMIT)
        val head = StringBuilder()
        if (firstByte in 0..0xFF) head.append(firstByte.toChar())
        head.append(readHttpHead(rawInput) ?: "")
        val headStr = head.toString()
        rawInput.reset()
        val line = headStr.lineSequence()
            ?.firstOrNull { it.lowercase().startsWith("proxy-authorization:") }
            ?: return false
        val b64 = line.substringAfter("Basic", "").trim()
        if (b64.isEmpty()) return false
        return try {
            val decoded = String(java.util.Base64.getDecoder().decode(b64), Charsets.UTF_8)
            val idx = decoded.indexOf(':')
            if (idx <= 0) false
            else TokenAuth.verifyProxyCreds(decoded.substring(0, idx), decoded.substring(idx + 1))
        } catch (_: IllegalArgumentException) {
            false
        }
    }

    private fun readHttpHead(input: InputStream): String? {
        val sb = StringBuilder()
        val b = ByteArray(1)
        var total = 0
        while (total < HEAD_LIMIT) {
            val n = input.read(b)
            if (n < 0) return null
            sb.append(b[0].toInt().toChar())
            total++
            if (sb.endsWith("\r\n\r\n")) return sb.toString()
        }
        return null
    }

    // ============================ Relay =============================== //

    /** toRemote=true means we are pumping client->internet (phone rx side). */
    private fun relay(a: Socket, b: Socket, toRemote: Boolean) {
        try {
            a.soTimeout = 0
            b.soTimeout = 0
        } catch (_: Exception) {}
        val j1 = scope.launch { pump(a, b, countAsRx = true) }
        val j2 = scope.launch { pump(b, a, countAsRx = false) }
        runBlocking { joinAll(j1, j2) }
        try { a.close() } catch (_: IOException) {}
        try { b.close() } catch (_: IOException) {}
    }

    private fun pump(src: Socket, dst: Socket, countAsRx: Boolean) {
        try {
            val input = src.getInputStream()
            val output = dst.getOutputStream()
            val buf = ByteArray(RELAY_BUF)
            while (true) {
                val n = input.read(buf)
                if (n < 0) break
                output.write(buf, 0, n)
                if (countAsRx) stats.addRx(n) else stats.addTx(n)
            }
            try { dst.shutdownOutput() } catch (_: IOException) {}
        } catch (_: IOException) {
            // direction torn down; the other pump finishes the session
        } catch (e: Exception) {
            Logger.d(TAG, "pump error: ${e.message}")
        }
    }

    /** Best-effort client-reachable address for SOCKS BND.ADDR replies. */
    private fun localAddressForClient(client: Socket): Pair<String, Int> {
        val port = Prefs.proxyPort
        val clientAddr = try {
            client.inetAddress?.address
        } catch (_: Exception) { null } ?: return "0.0.0.0" to port
        return try {
            val nis = NetworkInterface.getNetworkInterfaces()
            for (ni in nis) {
                if (!ni.isUp || ni.isLoopback) continue
                for (ia in ni.inetAddresses) {
                    if (ia is Inet4Address && samePrefix(ia.address, clientAddr)) {
                        return ia.hostAddress!! to port
                    }
                }
            }
            "0.0.0.0" to port
        } catch (_: Exception) {
            "0.0.0.0" to port
        }
    }

    private fun samePrefix(a: ByteArray, b: ByteArray): Boolean {
        if (a.size != b.size) return false
        for (i in 0 until min(3, a.size)) if (a[i] != b[i]) return false
        return true
    }

    private fun socksReply(out: OutputStream, rep: Int, host: String, port: Int) {
        val ip = try {
            InetAddress.getByName(host).address
        } catch (_: Exception) { byteArrayOf(0, 0, 0, 0) }
        val v4 = if (ip.size == 4) ip else byteArrayOf(0, 0, 0, 0)
        val reply = ByteArray(10)
        reply[0] = SOCKS_VER5.toByte()
        reply[1] = rep.toByte()
        reply[2] = 0x00
        reply[3] = 0x01 // IPv4
        v4.copyInto(reply, 4)
        reply[8] = (port shr 8).toByte()
        reply[9] = port.toByte()
        out.write(reply)
        out.flush()
    }
}
