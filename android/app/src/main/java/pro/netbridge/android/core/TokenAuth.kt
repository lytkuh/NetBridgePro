package pro.netbridge.android.core

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Shared-secret authentication used by both the proxy (RFC 1929 user/pass)
 * and the bridge (HMAC-SHA256 challenge / response).
 *
 * Bridge handshake:
 *   C->S  {"cmd":"auth","nonce":"<random>"}
 *   S->C  {"result":{"nonce":"<server-random>"}}
 *   C->S  {"cmd":"auth.verify","proof":hex(HMAC_SHA256(token, nonce + "|" + serverNonce))}
 *   S->C  {"ok":true}
 */
object TokenAuth {

    const val PROXY_USER = "netbridge"

    fun hmacSha256Hex(secret: String, message: String): String {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret.toByteArray(Charsets.UTF_8), "HmacSHA256"))
        return mac.doFinal(message.toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }
    }

    fun verifyProof(token: String, clientNonce: String, serverNonce: String, proof: String): Boolean {
        if (proof.length != 64) return false
        val expected = hmacSha256Hex(token, "$clientNonce|$serverNonce")
        return constantTimeEquals(expected, proof.lowercase())
    }

    /** RFC 1929 check: username must be PROXY_USER, password must be the token. */
    fun verifyProxyCreds(user: String, pass: String): Boolean =
        constantTimeEquals(PROXY_USER, user) && constantTimeEquals(Prefs.token, pass)

    private fun constantTimeEquals(a: String, b: String): Boolean {
        val ba = a.toByteArray(Charsets.UTF_8)
        val bb = b.toByteArray(Charsets.UTF_8)
        var diff = ba.size xor bb.size
        val n = maxOf(ba.size, bb.size)
        for (i in 0 until n) {
            diff = diff or ((ba.getOrElse(i) { 0 }).toInt() xor (bb.getOrElse(i) { 0 }).toInt())
        }
        return diff == 0
    }
}
