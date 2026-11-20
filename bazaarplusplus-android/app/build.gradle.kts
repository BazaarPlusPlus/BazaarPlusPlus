import groovy.json.JsonSlurper
import javax.inject.Inject

plugins {
    id("com.android.application")
}

@Suppress("UNCHECKED_CAST")
val lock = JsonSlurper().parseText(
    // Read through a provider: the configuration cache does not see a read that JsonSlurper
    // makes itself, and would keep the old tool versions after the lock changes.
    providers.fileContents(rootProject.layout.projectDirectory.file("toolchain.lock.json")).asText.get(),
) as Map<String, Any>
@Suppress("UNCHECKED_CAST")
val sdk = lock["androidSdk"] as Map<String, Any>
@Suppress("UNCHECKED_CAST")
val testDependencies = lock["testDependencies"] as Map<String, String>
val lockedBuildTools = sdk["buildTools"] as String
val abi = lock["abi"] as String

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
            // The release pipeline signs the APK with apksigner (ADR 0009).
            signingConfig = null
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    packaging {
        // Unity loads libmain.so by absolute path from nativeLibraryDir, so Android must extract it.
        jniLibs.useLegacyPackaging = true
    }

    androidResources {
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
        // The toolchain lock pins the supported SDK and plugin versions independently of lint's update notices.
        disable += listOf("OldTargetApi", "GradleDependency", "AndroidGradlePluginVersion")
    }
}

tasks.withType<Test>().configureEach {
    inputs.files(rootProject.fileTree("contracts")).withPropertyName("contracts")
    inputs.files(
        rootProject.fileTree("../release/fixtures/latest"),
        rootProject.fileTree("../release/fixtures/android-interop"),
        rootProject.fileTree("../release/fixtures/managed-payload"),
        rootProject.file("../release/manifest.ts"),
        rootProject.file("../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json"),
    ).withPropertyName("sharedContracts")
}

dependencies {
    testImplementation("junit:junit:${testDependencies["junit:junit"]}")
    // org.json is part of android.jar, which unit tests see only as stubs.
    testImplementation("org.json:json:${testDependencies["org.json:json"]}")
}

// D8 builds a separate dex so the bridge superclass resolves in the game class loader.
abstract class DexBridge : DefaultTask() {
    @get:InputFiles abstract val sources: ConfigurableFileCollection
    @get:InputFiles abstract val stubSources: ConfigurableFileCollection
    @get:InputFiles abstract val androidJar: ConfigurableFileCollection
    @get:InputFile abstract val d8: RegularFileProperty
    @get:InputFile abstract val java: RegularFileProperty
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
            // Invoke D8's Java entry point on every host, without a platform-specific shell wrapper.
            commandLine(listOf(java.get().asFile.path, "-cp", d8.get().asFile.path, "com.android.tools.r8.D8", "--min-api", "30") + libs + listOf("--classpath", stubs.path,
                "--output", dex.path) + classes.walkTopDown().filter { it.extension == "class" }.map { it.path })
        }
        val out = outputDir.get().asFile.apply { deleteRecursively(); mkdirs() }
        dex.resolve("classes.dex").copyTo(out.resolve("bridge.dex"))
    }
}

val javaLauncher = javaToolchains.launcherFor { languageVersion.set(JavaLanguageVersion.of((lock["jdk"] as Map<*, *>)["major"] as Int)) }
val javaCompiler = javaToolchains.compilerFor { languageVersion.set(JavaLanguageVersion.of((lock["jdk"] as Map<*, *>)["major"] as Int)) }

val dexBridge = tasks.register<DexBridge>("dexBridge") {
    sources.from(fileTree("src/bridge/java") { include("**/*.java") })
    stubSources.from(fileTree("src/unityStub/java") { include("**/*.java") })
    androidJar.from(androidComponents.sdkComponents.bootClasspath)
    d8.set(androidComponents.sdkComponents.sdkDirectory.map { it.file("build-tools/$lockedBuildTools/lib/d8.jar") })
    java.set(javaLauncher.map { it.executablePath })
    javac.set(javaCompiler.map { it.executablePath })
    workDir.set(layout.buildDirectory.dir("intermediates/bridge"))
}

androidComponents {
    beforeVariants(selector().withBuildType("release")) { variant ->
        variant.hostTests[com.android.build.api.variant.HostTestBuilder.UNIT_TEST_TYPE]?.enable = true
    }
    onVariants { variant ->
        variant.sources.assets?.addGeneratedSourceDirectory(dexBridge, DexBridge::outputDir)
    }
}
