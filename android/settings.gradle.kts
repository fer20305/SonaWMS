// Solución para AndroidLocationsException cuando existen ANDROID_PREFS_ROOT y ANDROID_USER_HOME
try {
    val processEnvClass = Class.forName("java.lang.ProcessEnvironment")
    val envField = processEnvClass.getDeclaredField("theEnvironment").apply { isAccessible = true }
    @Suppress("UNCHECKED_CAST")
    (envField.get(null) as? MutableMap<String, String>)?.remove("ANDROID_PREFS_ROOT")

    val ciEnvField = processEnvClass.getDeclaredField("theCaseInsensitiveEnvironment").apply { isAccessible = true }
    @Suppress("UNCHECKED_CAST")
    (ciEnvField.get(null) as? MutableMap<String, String>)?.remove("ANDROID_PREFS_ROOT")
} catch (_: Throwable) {}

pluginManagement {
    repositories {
        google()
        maven("https://maven-central.storage-download.googleapis.com/maven2/")
        gradlePluginPortal()
    }
}
plugins {
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        maven("https://maven-central.storage-download.googleapis.com/maven2/")
    }
}
rootProject.name = "SonaDeposito"
include(":app")

