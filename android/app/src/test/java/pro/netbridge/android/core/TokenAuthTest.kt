package pro.netbridge.android.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Technical tests for the HMAC-SHA256 bridge authentication primitives. */
class TokenAuthTest {

    @Test
    fun `hmac matches published RFC 4231-style vector`() {
        // HMAC-SHA256(key="key", msg="The quick brown fox jumps over the lazy dog")
        val expected = "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8"
        assertEquals(expected, TokenAuth.hmacSha256Hex("key", "The quick brown fox jumps over the lazy dog"))
    }

    @Test
    fun `hmac is deterministic and 64 hex chars`() {
        val a = TokenAuth.hmacSha256Hex("secret-token", "nonce1|nonce2")
        val b = TokenAuth.hmacSha256Hex("secret-token", "nonce1|nonce2")
        assertEquals(a, b)
        assertEquals(64, a.length)
        assertTrue(a.all { it.isDigit() || it in 'a'..'f' })
    }

    @Test
    fun `hmac changes when the message changes`() {
        val a = TokenAuth.hmacSha256Hex("token", "n1|n2")
        val b = TokenAuth.hmacSha256Hex("token", "n2|n1")
        assertFalse(a == b)
    }

    @Test
    fun `verifyProof accepts a correct proof`() {
        val c = "client-nonce-abc"
        val s = "server-nonce-xyz"
        val proof = TokenAuth.hmacSha256Hex("pairing-token", "$c|$s")
        assertTrue(TokenAuth.verifyProof("pairing-token", c, s, proof))
    }

    @Test
    fun `verifyProof is case-insensitive on the hex proof`() {
        val proof = TokenAuth.hmacSha256Hex("t", "c|s").uppercase()
        assertTrue(TokenAuth.verifyProof("t", "c", "s", proof))
    }

    @Test
    fun `verifyProof rejects wrong token`() {
        val proof = TokenAuth.hmacSha256Hex("right-token", "c|s")
        assertFalse(TokenAuth.verifyProof("wrong-token", "c", "s", proof))
    }

    @Test
    fun `verifyProof rejects wrong nonce order`() {
        val proof = TokenAuth.hmacSha256Hex("t", "c|s")
        assertFalse(TokenAuth.verifyProof("t", "s", "c", proof))
    }

    @Test
    fun `verifyProof rejects malformed proof lengths`() {
        val short = "abcd"
        val long = "a".repeat(65)
        assertFalse(TokenAuth.verifyProof("t", "c", "s", short))
        assertFalse(TokenAuth.verifyProof("t", "c", "s", long))
        assertFalse(TokenAuth.verifyProof("t", "c", "s", ""))
    }

    @Test
    fun `proxy auth username constant is stable`() {
        // The Windows companion hardcodes this user for RFC 1929.
        assertEquals("netbridge", TokenAuth.PROXY_USER)
    }
}
