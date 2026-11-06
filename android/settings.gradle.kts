pluginManagement {
    repositories {
        // Region-friendly mirrors first (dl.google.com / repo.maven.apache.org are
        // unreachable from some networks, e.g. IR/CN). Mirrors are public and
        // byte-identical to the originals; the originals stay as fallback.
        maven("https://maven.aliyun.com/repository/gradle-plugin")
        maven("https://maven.aliyun.com/repository/google")
        maven("https://maven.aliyun.com/repository/public")
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        maven("https://maven.aliyun.com/repository/google")
        maven("https://maven.aliyun.com/repository/public")
        google()
        mavenCentral()
    }
}

rootProject.name = "NetBridge"
include(":app")
