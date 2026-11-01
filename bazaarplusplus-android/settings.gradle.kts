pluginManagement {
    // toolchain.lock.json owns every tool version; the wrapper owns Gradle's.
    @Suppress("UNCHECKED_CAST")
    val lock = groovy.json.JsonSlurper().parse(file("toolchain.lock.json")) as Map<String, Any>
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
