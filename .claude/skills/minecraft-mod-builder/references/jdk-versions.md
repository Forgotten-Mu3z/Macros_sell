# JDK version matrix

Use this to decide which JDK the user needs based on their Minecraft target version. Verify against the template's `gradle.properties` if it disagrees — the template is authoritative for that specific version.

## Minecraft → JDK

| Minecraft version    | Required JDK | Notes                                          |
|----------------------|--------------|------------------------------------------------|
| 1.20.5 and newer     | 21+          | Mojang switched to Java 21 in 1.20.5.          |
| 1.18 – 1.20.4        | 17           | Java 17 introduced in 1.18.                    |
| 1.17                 | 16           | Brief Java 16 window before 1.18 moved to 17.  |
| 1.16.5 and older     | 8            | Legacy era.                                    |

For NeoForge specifically, the loader sometimes requires a newer JDK than vanilla Minecraft for the same MC version (e.g. NeoForge 26.x targeting 1.21.x requires JDK 25). **Always trust the MDK's `gradle.properties` `java_version` field** for NeoForge.

## Recommended JDK distributions

- **Adoptium Temurin** — `https://adoptium.net/` — works everywhere, free, no account needed.
- **Microsoft OpenJDK** — `https://learn.microsoft.com/en-us/java/openjdk/download` — NeoForge officially recommends this.
- **Azul Zulu** — `https://www.azul.com/downloads/` — also fine.

Avoid Oracle JDK unless the user has a license, and avoid JDKs from random package managers that might be 32-bit (Minecraft requires 64-bit).

## Verifying installation

```bash
java -version
```

The output should show the major version (e.g. `openjdk version "21.0.2"`) and `64-Bit`. If it shows 32-Bit, the user needs to install a 64-bit build.

## Multiple JDKs on one machine

If the user has multiple JDKs installed, they may need to point Gradle at the right one. Options:

- Set `JAVA_HOME` to the JDK path before running `./gradlew`.
- Add `org.gradle.java.home=/path/to/jdk` to `gradle.properties` (project-level or `~/.gradle/gradle.properties`).
- Use a JDK manager like SDKMAN (`https://sdkman.io/`) on macOS/Linux.

If `./gradlew build` fails with a confusing Java-version-related error, the very first thing to check is `./gradlew --version`, which shows which JVM Gradle picked up.
