package pro.netbridge.android.core

/**
 * Tiny reflection helper for platform APIs that are hidden from the SDK
 * (band numbers, NR state on some builds, OEM extras). Every call is failure
 * tolerant: a missing method simply yields null instead of crashing.
 */
object Reflect {

    fun call(instance: Any, methodName: String, vararg args: Any?): Any? = try {
        val types = args.map { it?.javaClass as Class<*>? }.toTypedArray()
        val m = findMethod(instance.javaClass, methodName, types)
        m?.isAccessible = true
        m?.invoke(instance, *args)
    } catch (_: Throwable) {
        null
    }

    fun callStatic(clazzName: String, methodName: String, vararg args: Any?): Any? = try {
        val clazz = Class.forName(clazzName)
        val types = args.map { it?.javaClass as Class<*>? }.toTypedArray()
        val m = findMethod(clazz, methodName, types)
        m?.isAccessible = true
        m?.invoke(null, *args)
    } catch (_: Throwable) {
        null
    }

    fun get(instance: Any, fieldName: String): Any? = try {
        val f = findField(instance.javaClass, fieldName)
        f?.isAccessible = true
        f?.get(instance)
    } catch (_: Throwable) {
        null
    }

    private fun findMethod(clazz: Class<*>, name: String, types: Array<Class<*>?>) =
        try {
            clazz.getMethod(name, *types)
        } catch (_: NoSuchMethodException) {
            // Retry ignoring primitive/wrapper mismatch by scanning declared methods.
            clazz.methods.firstOrNull { it.name == name && it.parameterCount == types.size }
        }

    private fun findField(clazz: Class<*>, name: String): java.lang.reflect.Field? {
        var c: Class<*>? = clazz
        while (c != null) {
            try {
                return c.getDeclaredField(name)
            } catch (_: NoSuchFieldException) {
                c = c.superclass
            }
        }
        return null
    }
}
