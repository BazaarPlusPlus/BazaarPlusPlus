pluginManagement {
    // toolchain.lock.json owns every tool version; the wrapper owns Gradle's.
    @Suppress("UNCHECKED_CAST")
    val lock = groovy.json.JsonSlurper().parseText(
        // Read through a provider, so that a lock change invalidates the configuration cache.
        providers.fileContents(layout.settingsDirectory.file("toolchain.lock.json")).asText.get(),
    ) as Map<String, Any>
    repositories {
        google()
        mavenCentral()
    }
    plugins {
        id("com.android.application") version (lock["androidGradlePlugin"] as String)
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "bazaarplusplus-android"
include(":app")
