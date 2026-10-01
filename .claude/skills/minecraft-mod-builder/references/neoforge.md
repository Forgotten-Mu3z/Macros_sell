# NeoForge reference

Loader-specific guidance for NeoForge. Read this when the user has chosen NeoForge in step 1 of the main skill.

NeoForge is the actively-maintained successor to Forge. If the user says "Forge," check whether they mean classic Forge (older versions only, no longer the recommended path) or NeoForge. For any modern MC version (1.20.2+), default to NeoForge.

## Key facts

- **Build tool**: Gradle, with either `ModDevGradle` (newer, recommended) or `NeoGradle` (older, still supported). Default to ModDevGradle unless the user has a reason not to.
- **Mappings**: Official Mojang mappings (no Yarn).
- **Mod metadata file**: `src/main/resources/META-INF/neoforge.mods.toml`
- **Entrypoint**: A class annotated with `@Mod("modid")`. Event registration uses the event bus from `FMLJavaModLoadingContext` (or `IEventBus` passed to the constructor in newer versions).
- **Per-version MDK templates**: `https://github.com/NeoForgeMDKs` — every supported MC version has its own template repo.

## Scaffolding (NeoForge)

The template lives at `https://github.com/NeoForgeMDKs/MDK-<mc-version>-ModDevGradle` (e.g. `MDK-1.21.10-ModDevGradle`).

1. Find the right template repo for the user's MC version. Browse `https://github.com/neoforgemdks` to confirm the version exists. If their target version doesn't have a template, web-search to find the nearest supported one and check whether NeoForge supports that MC version yet.
2. Download as ZIP or clone:
   ```bash
   git clone https://github.com/NeoForgeMDKs/MDK-<mc-version>-ModDevGradle.git <project-path>
   cd <project-path>
   rm -rf .git
   ```
3. The MDK comes pre-configured with a working example mod under the `com.example.examplemod` package.

## Renaming a NeoForge template

1. **`gradle.properties`** — set `mod_id`, `mod_name`, `mod_license`, `mod_version`, `mod_group_id`, `mod_authors`, `mod_description`. The MDK has comments next to each property explaining what it does — keep them or strip them, your call.
2. **`src/main/resources/META-INF/neoforge.mods.toml`** — most fields here are `${mod_id}`-style placeholders that Gradle fills in from `gradle.properties`, so you usually don't need to edit this file directly. Verify the `[[mods]]` and `[[dependencies.<modid>]]` sections look right.
3. **Package directories** — move `src/main/java/com/example/examplemod/` to the chosen package path. The example main class is typically called `ExampleMod` — rename it to match the user's mod name.
4. **Main class** — update the `@Mod(MODID)` annotation, the `MODID` constant inside the class, and the `package` declaration.
5. **Assets/data namespaces** — rename `src/main/resources/assets/examplemod/` and `data/examplemod/` to use the new mod ID.

Run `./gradlew build` to verify.

## Running the dev client (NeoForge)

```bash
./gradlew runClient
```

For a dedicated server: `./gradlew runServer`. The MDK provides `runClient`, `runServer`, `runData` (datagen), and `runGameTestServer` tasks.

## Registry pattern (NeoForge)

NeoForge uses **DeferredRegister** — you declare registry objects in static fields, and they're registered when the registry-specific event fires. This is unlike Fabric's immediate registration.

Example structure for an item:

```java
public class ModItems {
    public static final DeferredRegister.Items ITEMS =
        DeferredRegister.createItems(ExampleMod.MODID);

    public static final DeferredItem<Item> EXAMPLE_ITEM =
        ITEMS.registerSimpleItem("example_item");

    public static void register(IEventBus bus) {
        ITEMS.register(bus);
    }
}
```

Then in the main `@Mod` class constructor:

```java
public ExampleMod(IEventBus modEventBus, ModContainer modContainer) {
    ModItems.register(modEventBus);
}
```

## JDK note

NeoForge for 1.21+ requires **JDK 21 or newer**, and the most recent versions (26.x and later) require **JDK 25**. Check the MDK's `gradle.properties` for the exact `java_version` value — it's authoritative for that template.

## Useful NeoForge docs

- Official docs: `https://docs.neoforged.net/`
- Versioned docs (always check this matches your MC version): `https://docs.neoforged.net/docs/<version>/`
- MDK list: `https://github.com/neoforgemdks`
