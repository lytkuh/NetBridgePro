package pro.netbridge.android.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// NetBridge dark palette — mirrors the Windows client theme
private val Bg0 = Color(0xFF0E1116)
private val Bg1 = Color(0xFF151A24)
private val Accent = Color(0xFF22D3EE)
private val Accent2 = Color(0xFF34D399)
private val Warn = Color(0xFFFBBF24)
private val Danger = Color(0xFFF87171)
private val Text0 = Color(0xFFE6EAF2)
private val Text1 = Color(0xFF8B94A7)

private val DarkScheme = darkColorScheme(
    primary = Accent,
    onPrimary = Color(0xFF00232B),
    secondary = Accent2,
    onSecondary = Color(0xFF00291C),
    tertiary = Warn,
    background = Bg0,
    onBackground = Text0,
    surface = Bg1,
    onSurface = Text0,
    surfaceVariant = Color(0xFF1D2430),
    onSurfaceVariant = Text1,
    error = Danger,
    onError = Color(0xFF2B0000),
    outline = Color(0xFF232B3A)
)

private val LightScheme = lightColorScheme(
    primary = Color(0xFF0891B2),
    secondary = Color(0xFF059669),
    background = Color(0xFFF4F6FA),
    surface = Color.White,
    onBackground = Color(0xFF17202E),
    onSurface = Color(0xFF17202E),
    surfaceVariant = Color(0xFFE8ECF3),
    onSurfaceVariant = Color(0xFF57627A),
    outline = Color(0xFFC7CEDB)
)

@Composable
fun NetBridgeTheme(dark: Boolean = true, content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (dark) DarkScheme else LightScheme,
        content = content
    )
}
