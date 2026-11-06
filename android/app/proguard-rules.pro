# NetBridge Pro - ProGuard rules

# Keep crash-relevant line numbers
-keepattributes SourceFile,LineNumberTable

# org.json is part of the Android platform; nothing to keep.

# Compose + Coroutines ship consumer rules automatically.

# Reflection helpers resolve platform methods at runtime.
-keep class pro.netbridge.android.core.Reflect { *; }
