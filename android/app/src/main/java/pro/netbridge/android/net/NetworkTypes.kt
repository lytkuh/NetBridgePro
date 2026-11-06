package pro.netbridge.android.net

import android.telephony.TelephonyManager

/** Mapping between TelephonyManager network type ids and friendly names. */
object NetworkTypes {

    fun nameOf(type: Int): String = when (type) {
        TelephonyManager.NETWORK_TYPE_GPRS -> "GPRS"
        TelephonyManager.NETWORK_TYPE_EDGE -> "EDGE"
        TelephonyManager.NETWORK_TYPE_UMTS -> "UMTS"
        TelephonyManager.NETWORK_TYPE_CDMA -> "CDMA"
        TelephonyManager.NETWORK_TYPE_EVDO_0 -> "EVDO_0"
        TelephonyManager.NETWORK_TYPE_EVDO_A -> "EVDO_A"
        TelephonyManager.NETWORK_TYPE_1xRTT -> "1xRTT"
        TelephonyManager.NETWORK_TYPE_HSDPA -> "HSDPA"
        TelephonyManager.NETWORK_TYPE_HSUPA -> "HSUPA"
        TelephonyManager.NETWORK_TYPE_HSPA -> "HSPA"
        TelephonyManager.NETWORK_TYPE_IDEN -> "IDEN"
        TelephonyManager.NETWORK_TYPE_EVDO_B -> "EVDO_B"
        TelephonyManager.NETWORK_TYPE_LTE -> "LTE"
        TelephonyManager.NETWORK_TYPE_EHRPD -> "EHRPD"
        TelephonyManager.NETWORK_TYPE_HSPAP -> "HSPAP"
        TelephonyManager.NETWORK_TYPE_GSM -> "GSM"
        TelephonyManager.NETWORK_TYPE_TD_SCDMA -> "TD_SCDMA"
        TelephonyManager.NETWORK_TYPE_IWLAN -> "IWLAN"
        19 -> "LTE_CA"
        20 -> "NR"
        else -> "UNKNOWN"
    }

    fun familyOf(name: String): String = when (name) {
        "GPRS", "EDGE", "GSM", "IDEN", "CDMA", "1xRTT" -> "2G"
        "UMTS", "HSDPA", "HSUPA", "HSPA", "HSPAP", "EVDO_0", "EVDO_A", "EVDO_B", "EHRPD", "TD_SCDMA" -> "3G"
        "LTE", "LTE_CA" -> "4G"
        "NR" -> "5G"
        "IWLAN" -> "WLAN"
        else -> "UNKNOWN"
    }

    /** 3GPP preferred_network_mode values (RILConstants). Values marked VERIFY
     *  are OEM-dependent and are confirmed by the companion after writing. */
    object RilMode {
        const val AUTO_5G_DEFAULT = 26     // NR/LTE/TDSCDMA/CDMA/EVDO/GSM/WCDMA (most OEM builds)
        const val AUTO_5G_AOSP = 22        // NR/LTE/TDSCDMA/CDMA/EVDO/GSM/WCDMA (AOSP numbering on many builds)
        const val AUTO_5G_ALT = 33         // seen on some Samsung/MTK builds
        const val NR_LTE_GSM_WCDMA = 20
        const val NR_ONLY = 17
        const val NR_LTE = 18
        const val LTE_ONLY = 11
        const val AUTO_4G = 9              // LTE/GSM/WCDMA
        const val WCDMA_ONLY = 2
        const val GSM_ONLY = 1
        const val AUTO_3G_2G = 3
        const val WCDMA_PREF = 0
    }

    /** Ordered candidate values per abstract mode; first value that verifies wins. */
    val candidatesFor: Map<String, List<Int>> = mapOf(
        "5G_AUTO" to listOf(RilMode.AUTO_5G_DEFAULT, RilMode.AUTO_5G_AOSP, RilMode.AUTO_5G_ALT, RilMode.NR_LTE_GSM_WCDMA),
        "5G_4G" to listOf(RilMode.NR_LTE, RilMode.AUTO_5G_AOSP),
        "5G_ONLY" to listOf(RilMode.NR_ONLY),
        "4G_ONLY" to listOf(RilMode.LTE_ONLY),
        "4G_AUTO" to listOf(RilMode.AUTO_4G),
        "3G_ONLY" to listOf(RilMode.WCDMA_ONLY),
        "3G_2G" to listOf(RilMode.AUTO_3G_2G),
        "2G_ONLY" to listOf(RilMode.GSM_ONLY),
        "DEFAULT" to listOf(RilMode.WCDMA_PREF)
    )
}
