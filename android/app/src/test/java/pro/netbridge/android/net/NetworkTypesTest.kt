package pro.netbridge.android.net

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Tests for RIL mode candidates and radio-type naming/family mapping. */
class NetworkTypesTest {

    @Test
    fun `known radio type ids map to names`() {
        assertEquals("GPRS", NetworkTypes.nameOf(1))
        assertEquals("EDGE", NetworkTypes.nameOf(2))
        assertEquals("UMTS", NetworkTypes.nameOf(3))
        assertEquals("LTE", NetworkTypes.nameOf(13))
        assertEquals("LTE_CA", NetworkTypes.nameOf(19))
        assertEquals("NR", NetworkTypes.nameOf(20))
    }

    @Test
    fun `unknown radio type id falls back safely`() {
        assertEquals("UNKNOWN", NetworkTypes.nameOf(123))
        assertEquals("UNKNOWN", NetworkTypes.nameOf(-1))
    }

    @Test
    fun `families group correctly`() {
        assertEquals("2G", NetworkTypes.familyOf("GPRS"))
        assertEquals("2G", NetworkTypes.familyOf("EDGE"))
        assertEquals("3G", NetworkTypes.familyOf("HSPA"))
        assertEquals("4G", NetworkTypes.familyOf("LTE"))
        assertEquals("4G", NetworkTypes.familyOf("LTE_CA"))
        assertEquals("5G", NetworkTypes.familyOf("NR"))
        assertEquals("WLAN", NetworkTypes.familyOf("IWLAN"))
        assertEquals("UNKNOWN", NetworkTypes.familyOf("NOT_A_RAT"))
    }

    @Test
    fun `every abstract mode has at least one candidate`() {
        val modes = listOf("5G_AUTO", "5G_4G", "5G_ONLY", "4G_AUTO", "4G_ONLY", "3G_ONLY", "3G_2G", "2G_ONLY")
        for (m in modes) {
            assertTrue("mode $m has no candidates", NetworkTypes.candidatesFor[m].orEmpty().isNotEmpty())
        }
    }

    @Test
    fun `5G auto prefers the common OEM value 26 first`() {
        val c = NetworkTypes.candidatesFor["5G_AUTO"].orEmpty()
        assertEquals(26, c.first())
        assertTrue(c.contains(22)) // AOSP numbering fallback
    }

    @Test
    fun `candidate values are positive and unique per mode`() {
        for ((mode, values) in NetworkTypes.candidatesFor) {
            assertEquals("mode $mode has duplicate candidates", values.size, values.distinct().size)
            assertTrue("mode $mode has non-positive RIL value", values.all { it >= 0 })
        }
    }

    @Test
    fun `lte only and gsm only use canonical RIL values`() {
        assertEquals(listOf(11), NetworkTypes.candidatesFor["4G_ONLY"])
        assertEquals(listOf(1), NetworkTypes.candidatesFor["2G_ONLY"])
    }
}
