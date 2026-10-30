import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "ar.com.sona.deposito"
    compileSdk = 35

    defaultConfig {
        applicationId = "ar.com.sona.deposito"
        minSdk = 26
        targetSdk = 35
        versionCode = 23
        versionName = "1.22"
    }

    // Firma de producción: NO está en el repositorio. Se pone en android/firma/sona.keystore y sus datos en
    // android/firma/keystore.properties (storePassword, keyAlias, keyPassword). Siempre la MISMA clave:
    // si cambia la firma, el APK nuevo no se instala encima del anterior en los celulares. Sin firma, se usa la de debug.
    val firmaSona = rootProject.file("firma/sona.keystore")
    val datosFirma = Properties().apply {
        val f = rootProject.file("firma/keystore.properties")
        if (f.exists()) f.inputStream().use { load(it) }
    }
    signingConfigs {
        if (firmaSona.exists()) create("sona") {
            storeFile = firmaSona
            storePassword = datosFirma.getProperty("storePassword") ?: System.getenv("SONA_STORE_PASSWORD") ?: "android"
            keyAlias = datosFirma.getProperty("keyAlias") ?: "androiddebugkey"
            keyPassword = datosFirma.getProperty("keyPassword") ?: System.getenv("SONA_KEY_PASSWORD") ?: "android"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (firmaSona.exists()) signingConfigs.getByName("sona") else signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.ui:ui-tooling-preview")
    debugImplementation("androidx.compose.ui:ui-tooling")

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    // Cámara + lectura de QR/códigos de barra (modelo incluido, no necesita Google Play)
    val camerax = "1.4.1"
    implementation("androidx.camera:camera-camera2:$camerax")
    implementation("androidx.camera:camera-lifecycle:$camerax")
    implementation("androidx.camera:camera-view:$camerax")
    implementation("com.google.mlkit:barcode-scanning:17.3.0")

    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    // Medir bultos con realidad aumentada (opcional: solo si el celular tiene ARCore / Servicios de Google Play para RA)
    implementation("com.google.ar:core:1.45.0")

    testImplementation("junit:junit:4.13.2")
}
