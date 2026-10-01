# Recipe: simple entity

Add a basic mob entity. **This is the most complex feature in this skill** — entities require a Java class, a renderer, a model, a texture, animations (optional), spawn logic, and attribute registration. Plan on this taking significantly longer than items or blocks.

If the user is asking for a simple "thing that exists in the world" rather than a creature, consider whether a custom block or a block entity would meet their need first.

## What you'll create

- An entity class extending `LivingEntity`, `Mob`, `Animal`, or similar.
- Entity type registration.
- Attribute registration (health, speed, damage, etc.).
- A renderer class (client-side only).
- An entity model.
- A texture.
- Spawn egg item (optional but recommended for testing).

## Step 1: Ask the user

- **Entity ID** (snake_case).
- **Behavior**: passive (like a cow), hostile (like a zombie), or just a static decoration?
- **Base class**: pick one based on behavior — `Animal` for passive, `Monster` for hostile, `PathfinderMob` for custom.
- **Health, speed, attack damage** (give them sensible defaults: 10 health, 0.25 speed, 2.0 damage).

## Step 2: Outline the work

Because entity setup is substantial, walk the user through it in **sub-steps**, running `./gradlew build` (not `runClient`) after each one to catch errors early:

1. Create the entity class.
2. Register the entity type.
3. Register entity attributes.
4. Create and register the renderer (client side).
5. Create the model class.
6. **Generate the entity texture** (don't leave this for later — entities without textures render as completely invisible or fall back to the missing-texture checker, both of which read as "broken"). Entity textures are NOT 16×16 like items — the size depends on the model's UV layout, typically 64×64 or 64×32 for humanoid-ish models. Match the dimensions the model class declares. Save at `src/main/resources/assets/<mod_id>/textures/entity/<entity_id>.png`.
7. (Optional) Add a spawn egg. **The spawn egg uses the vanilla `spawn_egg` template item model, which auto-colors from two color values you set during registration — no separate texture file needed.** Pick two contrasting RGB hex colors that fit the entity (e.g. dark red base + bright orange spots for a fire creature) and pass them to the spawn egg's properties.
8. (Optional) Add natural spawning to biomes.

## Step 3: Read the loader-specific docs before writing code

Entity APIs change more between MC versions than item/block APIs. Before generating any code, web-search:

- Fabric: `https://docs.fabricmc.net/develop/entities/effects/` and related entity pages, filtered by MC version.
- NeoForge: `https://docs.neoforged.net/docs/<version>/concepts/entities/`

Both have specific examples that are likely more current than anything you'd generate from memory. Pull the registration patterns from the docs and adapt them to the user's chosen names.

## Step 4: Iterate

After getting the entity to register and render, the user will likely want to refine: AI goals, sounds, drops, animations. Each of those is a separate small task — handle them on request, one at a time, rebuilding between each.

## Why this recipe is shorter than the others

Entity work has too many version- and behavior-specific branches to fit a single recipe usefully. The recipe's value here is making the user aware of the scope before they commit, and pointing at the right docs for their loader and version. Generate code from the docs rather than from memory.
