import groovy.json.JsonSlurper
import javax.inject.Inject

plugins {
    id("com.android.application")
}

@Suppress("UNCHECKED_CAST")
val lock = JsonSlurper().parse(rootProject.file("toolchain.lock.json")) as Map<String, Any>
@Suppress("UNCHECKED_CAST")
val sdk = lock["androidSdk"] as Map<String, Any>
@Suppress("UNCHECKED_CAST")
val testDependencies = lock["testDependencies"] as Map<String, String>
val lockedBuildTools = sdk["buildTools"] as String
val abi = lock["abi"] as String

// The release pipeline (release/android-app.mjs) passes the product version, the versionCode
// it derives from it, and the staged Android App Runtime; a development build has none of them
// and runs with native launch only until a runtime is staged (`just android::stage-runtime`).
val productVersion = providers.gradleProperty("bpp.version").orElse("0.0.0")
val appVersionCode = providers.gradleProperty("bpp.versionCode").map { it.toInt() }.orElse(1)
val runtimeDir = providers.gradleProperty("bpp.runtimeDir").map { rootProject.file(it) }
    .orElse(provider { rootProject.file("build/runtime").takeIf { it.isDirectory } ?: rootProject.file("build/no-runtime") })

android {
    namespace = "com.bazaarplusplus.android"
    compileSdk = (sdk["compileSdk"] as Number).toInt()
    buildToolsVersion = lockedBuildTools

    defaultConfig {
        applicationId = "com.bazaarplusplus.android"
        minSdk = (sdk["minSdk"] as Number).toInt()
        targetSdk = (sdk["targetSdk"] as Number).toInt()
        versionCode = appVersionCode.get()
        versionName = productVersion.get()
        ndk { abiFilters += abi }
    }

    buildTypes {
        debug {
            applicationIdSuffix = ".dev"
            versionNameSuffix = "-dev"
        }
        release {
            isMinifyEnabled = false
            // Signed by apksigner in the release pipeline with the Android release key (ADR 0009).
            signingConfig = null
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    packaging {
        // Unity loads libmain.so by absolute path from nativeLibraryDir, and the App links the
        // game to its own copies there, so native libraries must be extracted on install.
        jniLibs.useLegacyPackaging = true
    }

    androidResources {
        // The runtime zip and the bridge dex are opened in place or copied byte for byte.
        noCompress += listOf("zip", "dex")
    }

    sourceSets["main"].assets.directories += runtimeDir.get().resolve("assets").path
    sourceSets["main"].jniLibs.directories += runtimeDir.get().resolve("jniLibs").path

    testOptions {
        unitTests.isReturnDefaultValues = true
    }

    lint {
        abortOnError = true
        warningsAsErrors = true
        checkReleaseBuilds = true
        // The App targets the SDK the toolchain lock pins, not the newest one lint knows.
        disable += listOf("OldTargetApi", "GradleDependency", "AndroidGradlePluginVersion")
    }
}

dependencies {
    testImplementation("junit:junit:${testDependencies["junit:junit"]}")
    // org.json is part of android.jar, which unit tests see only as stubs.
    testImplementation("org.json:json:${testDependencies["org.json:json"]}")
}

/**
 * Compiles the bridge Activity into its own dex. It must live outside the App's classes.dex:
 * the game class loader defines it together with the game's APKs, so its superclass resolves to
 * the installed game's UnityPlayerActivity. The Unity stub is compile-only.
 */
abstract class DexBridge : DefaultTask() {
    @get:InputFiles abstract val sources: ConfigurableFileCollection
    @get:InputFiles abstract val stubSources: ConfigurableFileCollection
    @get:InputFiles abstract val androidJar: ConfigurableFileCollection
    @get:InputFile abstract val d8: RegularFileProperty
    @get:InputFile abstract val javac: RegularFileProperty
    @get:OutputDirectory abstract val outputDir: DirectoryProperty
    @get:Internal abstract val workDir: DirectoryProperty
    @get:Inject abstract val exec: ExecOperations

    @TaskAction
    fun run() {
        val work = workDir.get().asFile.apply { deleteRecursively(); mkdirs() }
        val stubs = work.resolve("stubs").apply { mkdirs() }
        val classes = work.resolve("classes").apply { mkdirs() }
        val dex = work.resolve("dex").apply { mkdirs() }
        val jar = androidJar.files.joinToString(File.pathSeparator) { it.path }
        val libs = androidJar.files.flatMap { listOf("--lib", it.path) }
        exec.exec {
            commandLine(listOf(javac.get().asFile.path, "-nowarn", "--release", "17", "-cp", jar, "-d", stubs.path) +
                stubSources.files.map { it.path })
        }
        exec.exec {
            commandLine(listOf(javac.get().asFile.path, "-nowarn", "--release", "17", "-cp", jar + File.pathSeparator + stubs.path,
                "-d", classes.path) + sources.files.map { it.path })
        }
        exec.exec {
            commandLine(listOf(d8.get().asFile.path, "--min-api", "30") + libs + listOf("--classpath", stubs.path,
                "--output", dex.path) + classes.walkTopDown().filter { it.extension == "class" }.map { it.path })
        }
        val out = outputDir.get().asFile.apply { deleteRecursively(); mkdirs() }
        dex.resolve("classes.dex").copyTo(out.resolve("bridge.dex"))
    }
}

val javaLauncher = javaToolchains.launcherFor { languageVersion.set(JavaLanguageVersion.of((lock["jdk"] as Map<*, *>)["major"] as Int)) }

val dexBridge = tasks.register<DexBridge>("dexBridge") {
    sources.from(fileTree("src/bridge/java") { include("**/*.java") })
    stubSources.from(fileTree("src/unityStub/java") { include("**/*.java") })
    androidJar.from(androidComponents.sdkComponents.bootClasspath)
    d8.set(androidComponents.sdkComponents.sdkDirectory.map { it.file("build-tools/$lockedBuildTools/d8") })
    javac.set(javaLauncher.map { it.metadata.installationPath.file("bin/javac") })
    workDir.set(layout.buildDirectory.dir("intermediates/bridge"))
}

androidComponents {
    onVariants { variant ->
        variant.sources.assets?.addGeneratedSourceDirectory(dexBridge, DexBridge::outputDir)
    }
}
