package ar.com.sona.deposito.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Configuración del celular: servidores (local y externo), usuario y clave guardados para reconectar solo,
 * y el id del dispositivo. La clave se guarda cifrada con el almacén de claves de Android.
 */
class Ajustes(context: Context) {
    private val prefs = context.getSharedPreferences("sona_deposito", Context.MODE_PRIVATE)

    var servidorLocal: String
        get() = prefs.getString("servidorLocal", "http://10.0.50.207:8080") ?: ""
        set(v) = prefs.edit().putString("servidorLocal", v.trim().trimEnd('/')).apply()
    var servidorExterno: String
        get() = prefs.getString("servidorExterno", "http://203.0.113.10:8080") ?: ""
        set(v) = prefs.edit().putString("servidorExterno", v.trim().trimEnd('/')).apply()
    /** Último servidor que respondió (se prueba primero). */
    var servidorActivo: String
        get() = prefs.getString("servidorActivo", "") ?: ""
        set(v) = prefs.edit().putString("servidorActivo", v).apply()

    var usuario: String
        get() = prefs.getString("usuario", "") ?: ""
        set(v) = prefs.edit().putString("usuario", v.trim().uppercase()).apply()
    var clave: String
        get() = prefs.getString("clave", null)?.let { descifrar(it) } ?: ""
        set(v) = prefs.edit().putString("clave", if (v.isEmpty()) null else cifrar(v)).apply()
    var token: String
        get() = prefs.getString("token", "") ?: ""
        set(v) = prefs.edit().putString("token", v).apply()
    var rol: String
        get() = prefs.getString("rol", "") ?: ""
        set(v) = prefs.edit().putString("rol", v).apply()
    /** Permisos del usuario (ZUSR) separados por coma; vacío = servidor viejo sin permisos (se usa el rol). */
    var permisos: String
        get() = prefs.getString("permisos", "") ?: ""
        set(v) = prefs.edit().putString("permisos", v).apply()
    /** Foto de "Reconocer por foto" que se está sacando (ruta y hora): si Android cierra la app mientras la cámara está abierta, al volver se retoma. */
    var fotoIaPendiente: String
        get() = prefs.getString("fotoIaPendiente", "") ?: ""
        set(v) = prefs.edit().putString("fotoIaPendiente", v).putLong("fotoIaHora", System.currentTimeMillis()).commit().let { }
    val fotoIaHora: Long get() = prefs.getLong("fotoIaHora", 0)
    var nombre: String
        get() = prefs.getString("nombre", "") ?: ""
        set(v) = prefs.edit().putString("nombre", v).apply()
    var empresaPicking: Int
        get() = prefs.getInt("empresaPicking", 0)
        set(v) = prefs.edit().putInt("empresaPicking", v).apply()
    var linterna: Boolean
        get() = prefs.getBoolean("linterna", false)
        set(v) = prefs.edit().putBoolean("linterna", v).apply()

    val dispositivo: String
        get() = prefs.getString("dispositivo", null) ?: UUID.randomUUID().toString().also { prefs.edit().putString("dispositivo", it).apply() }

    val servidores: List<String>
        get() = listOf(servidorActivo, servidorLocal, servidorExterno).filter { it.isNotBlank() }.distinct()

    fun cerrarSesion(olvidarClave: Boolean) {
        token = ""
        if (olvidarClave) clave = ""
    }

    // ------------------------------------------------------------------ cifrado
    private fun llave(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build()
        )
        return gen.generateKey()
    }

    private fun cifrar(t: String): String = try {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, llave())
        val datos = c.doFinal(t.toByteArray())
        Base64.encodeToString(c.iv, Base64.NO_WRAP) + ":" + Base64.encodeToString(datos, Base64.NO_WRAP)
    } catch (e: Exception) {
        "plano:" + t
    }

    private fun descifrar(s: String): String = try {
        if (s.startsWith("plano:")) s.removePrefix("plano:") else {
            val (iv, datos) = s.split(":")
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, llave(), GCMParameterSpec(128, Base64.decode(iv, Base64.NO_WRAP)))
            String(c.doFinal(Base64.decode(datos, Base64.NO_WRAP)))
        }
    } catch (e: Exception) {
        ""
    }

    companion object { private const val ALIAS = "sona_deposito_clave" }
}
