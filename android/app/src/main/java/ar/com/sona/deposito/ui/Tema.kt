package ar.com.sona.deposito.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/** Colores de marca SONA: azul marino y naranja. Verde/amarillo/rojo solo para estados. */
object Colores {
    val Marino = Color(0xFF0B2A42)
    val Marino2 = Color(0xFF1C4C73)
    val Naranja = Color(0xFFF47920)
    val NaranjaClaro = Color(0xFFFFE7D4)
    val Fondo = Color(0xFFEEF1F4)
    val Panel = Color(0xFFFFFFFF)
    val Linea = Color(0xFFC9D1D9)
    val Texto = Color(0xFF1B2733)
    val Tenue = Color(0xFF5D6B79)
    val Ok = Color(0xFF1E8E3E)
    val OkClaro = Color(0xFFE3F4E8)
    val Aviso = Color(0xFFB7791F)
    val AvisoClaro = Color(0xFFFFF5DB)
    val Error = Color(0xFFC62828)
    val ErrorClaro = Color(0xFFFDE7E7)
    val Info = Color(0xFF1565C0)
    val InfoClaro = Color(0xFFE3EEFB)
}

@Composable
fun TemaSona(contenido: @Composable () -> Unit) {
    val esquema = lightColorScheme(
        primary = Colores.Marino,
        onPrimary = Color.White,
        secondary = Colores.Naranja,
        onSecondary = Color.White,
        tertiary = Colores.Naranja,
        background = Colores.Fondo,
        onBackground = Colores.Texto,
        surface = Colores.Panel,
        onSurface = Colores.Texto,
        surfaceVariant = Color(0xFFE4E9EE),
        onSurfaceVariant = Colores.Tenue,
        outline = Colores.Linea,
        error = Colores.Error,
    )
    val base = Typography()
    val tipografia = base.copy(
        headlineMedium = TextStyle(fontSize = 26.sp, fontWeight = FontWeight.Bold),
        titleLarge = TextStyle(fontSize = 21.sp, fontWeight = FontWeight.Bold),
        titleMedium = TextStyle(fontSize = 17.sp, fontWeight = FontWeight.SemiBold),
        bodyLarge = TextStyle(fontSize = 16.sp),
    )
    MaterialTheme(colorScheme = esquema, typography = tipografia, content = contenido)
}
