package pro.netbridge.android.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Apartment
import androidx.compose.material.icons.filled.Bolt
import androidx.compose.material.icons.filled.Code
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Public
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import pro.netbridge.android.BuildConfig
import pro.netbridge.android.R

private const val SITE_URL = "https://lytkuh.ir"
private const val GITHUB_URL = "https://github.com/lytkuh/NetBridgePro"

@Composable
fun AboutScreen() {
    val context = LocalContext.current
    val openUrl: (String) -> Unit = { url ->
        try {
            context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
        } catch (_: Exception) {
            // no browser on device — ignore
        }
    }
    val openSite = { openUrl(SITE_URL) }
    val openGithub = { openUrl(GITHUB_URL) }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(vertical = 8.dp)
    ) {
        // ---- Identity header: logo + name + version ---- //
        Card(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 6.dp),
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
            shape = RoundedCornerShape(16.dp)
        ) {
            Column(
                Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Image(
                    painter = painterResource(R.drawable.ic_logo_full),
                    contentDescription = "NetBridge Pro logo",
                    modifier = Modifier.size(96.dp)
                )
                Spacer(Modifier.height(10.dp))
                Text(
                    stringResource(R.string.app_name),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Spacer(Modifier.height(2.dp))
                Text(
                    stringResource(R.string.about_tagline),
                    fontSize = 12.sp,
                    textAlign = TextAlign.Center,
                    color = MaterialTheme.colorScheme.primary
                )
                Spacer(Modifier.height(6.dp))
                Text(
                    stringResource(R.string.about_version, BuildConfig.VERSION_NAME),
                    fontSize = 11.sp,
                    fontFamily = FontFamily.Monospace,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }

        // ---- Project description ---- //
        Card2(stringResource(R.string.about_what_title)) {
            Text(
                stringResource(R.string.about_what_body),
                fontSize = 13.sp,
                lineHeight = 20.sp,
                color = MaterialTheme.colorScheme.onSurface
            )
            Spacer(Modifier.height(8.dp))
            Text(
                stringResource(R.string.about_what_body2),
                fontSize = 12.sp,
                lineHeight = 18.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        // ---- Credits ---- //
        Card2(stringResource(R.string.about_credits_title)) {
            CreditRow(
                icon = { Icon(Icons.Filled.Person, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp)) },
                label = stringResource(R.string.about_developer),
                value = stringResource(R.string.about_developer_name)
            )
            CreditRow(
                icon = { Icon(Icons.Filled.Public, null, tint = MaterialTheme.colorScheme.secondary, modifier = Modifier.size(20.dp)) },
                label = stringResource(R.string.about_website),
                value = "lytkuh.ir",
                onClick = openSite,
                latinValue = true
            )
            CreditRow(
                icon = { Icon(Icons.Filled.Code, null, tint = Color(0xFFC9D1D9), modifier = Modifier.size(20.dp)) },
                label = stringResource(R.string.about_github),
                value = "github.com/lytkuh/NetBridgePro",
                onClick = openGithub,
                latinValue = true
            )
            CreditRow(
                icon = { Icon(Icons.Filled.Bolt, null, tint = Color(0xFFFBBF24), modifier = Modifier.size(20.dp)) },
                label = stringResource(R.string.about_powered),
                value = stringResource(R.string.about_powered_name)
            )
            CreditRow(
                icon = { Icon(Icons.Filled.Apartment, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp)) },
                label = stringResource(R.string.about_subsidiary),
                value = stringResource(R.string.about_subsidiary_name)
            )
        }

        // ---- License footer ---- //
        Text(
            stringResource(R.string.about_footer),
            fontSize = 11.sp,
            textAlign = TextAlign.Center,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 10.dp, bottom = 18.dp)
        )
    }
}

@Composable
private fun CreditRow(
    icon: @Composable () -> Unit,
    label: String,
    value: String,
    onClick: (() -> Unit)? = null,
    latinValue: Boolean = false
) {
    val rtl = LocalLayoutDirection.current == LayoutDirection.Rtl
    Row(
        Modifier
            .fillMaxWidth()
            .then(if (onClick != null) Modifier.clickable { onClick() } else Modifier)
            .padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            Modifier
                .size(34.dp)
                .background(
                    MaterialTheme.colorScheme.surfaceVariant,
                    CircleShape
                ),
            contentAlignment = Alignment.Center
        ) { icon() }
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text(label, fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(
                value,
                fontSize = 14.sp,
                fontWeight = FontWeight.SemiBold,
                style = if (latinValue) TextStyle(textDirection = TextDirection.Ltr) else TextStyle.Default,
                color = if (onClick != null) MaterialTheme.colorScheme.secondary
                else MaterialTheme.colorScheme.onSurface
            )
        }
        if (onClick != null) {
            // mirror the "open" arrow so it points outward in RTL too
            Text(if (rtl) "↖" else "↗", fontSize = 14.sp, color = MaterialTheme.colorScheme.secondary)
        }
    }
}
