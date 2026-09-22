# Audio provenance

Downloaded 2026-09-21. Every shipped clip is **CC0 1.0 Universal**. Source pages were checked individually; Kenney archive licence files were also checked and are retained in `Licenses/`. No paid or attribution-required asset is included. Conversion details are reproducible in `prepare_clips.py` (offline only).

All positional clips are downmixed to mono; all files are Ogg Vorbis. Short fades are applied to one-shots. Flight engine/alarm loops have a 50ms wrap crossfade. The city bed is already a supplied near-seamless loop. Music retains the full track with short edge fades; it repeats rather than being claimed a musically seamless loop. No human has auditioned this mix.

| Shipped file | Original file | Author | Source URL | Licence | Download date |
|---|---|---|---|---|---|
| punch-1.ogg | impactPunch_heavy_000.ogg | Kenney | [Impact Sounds][impact] | CC0 | 2026-09-21 |
| punch-2.ogg | impactPunch_heavy_001.ogg | Kenney | [Impact Sounds][impact] | CC0 | 2026-09-21 |
| step-1.ogg | footstep_concrete_000.ogg | Kenney | [Impact Sounds][impact] | CC0 | 2026-09-21 |
| step-2.ogg | footstep_concrete_001.ogg | Kenney | [Impact Sounds][impact] | CC0 | 2026-09-21 |
| step-3.ogg | footstep_concrete_002.ogg | Kenney | [Impact Sounds][impact] | CC0 | 2026-09-21 |
| land.ogg | impactSoft_heavy_000.ogg | Kenney | [Impact Sounds][impact] | CC0 | 2026-09-21 |
| hit.ogg | impactPunch_medium_000.ogg | Kenney | [Impact Sounds][impact] | CC0 | 2026-09-21 |
| flight-start.ogg | spaceEngineSmall_000.ogg, first 0.65s | Kenney | [Sci-fi Sounds][scifi] | CC0 | 2026-09-21 |
| flight-loop.ogg | spaceEngineLow_000.ogg | Kenney | [Sci-fi Sounds][scifi] | CC0 | 2026-09-21 |
| debris.ogg | explosionCrunch_000.ogg | Kenney | [Sci-fi Sounds][scifi] | CC0 | 2026-09-21 |
| fire.ogg | explosionCrunch_002.ogg | Kenney | [Sci-fi Sounds][scifi] | CC0 | 2026-09-21 |
| ice.ogg | forceField_000.ogg | Kenney | [Sci-fi Sounds][scifi] | CC0 | 2026-09-21 |
| telekinesis.ogg | forceField_003.ogg | Kenney | [Sci-fi Sounds][scifi] | CC0 | 2026-09-21 |
| death.ogg | error_006.ogg | Kenney | [Interface Sounds][ui] | CC0 | 2026-09-21 |
| jump.ogg | switch_001.ogg | Kenney | [Interface Sounds][ui] | CC0 | 2026-09-21 |
| ui-click.ogg | click_001.ogg | Kenney | [Interface Sounds][ui] | CC0 | 2026-09-21 |
| ui-hover.ogg | select_001.ogg | Kenney | [Interface Sounds][ui] | CC0 | 2026-09-21 |
| city-bed.ogg | busy_cyberworld.ogg | TinyWorlds | [Scifi City – Ambient Loop][city] | CC0 | 2026-09-21 |
| siren-bed.ogg | alarm.wav | Frenchyboy | [Alarm][alarm] | CC0 | 2026-09-21 |
| music.ogg | Polygraph City.mp3 | RawGames / RawGameStudios | [Polygraph City][music] | CC0 | 2026-09-21 |
| gunshot.ogg | Prepared SFX Library/1911/A_34P.wav, 0.8s from first report | Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney | [Free Firearm Sound Library][firearms] | CC0 | 2026-09-21 |

[impact]: https://kenney.nl/assets/impact-sounds
[scifi]: https://kenney.nl/assets/sci-fi-sounds
[ui]: https://kenney.nl/assets/interface-sounds
[city]: https://opengameart.org/content/scifi-city-ambient-loop
[alarm]: https://opengameart.org/content/alarm-2
[music]: https://opengameart.org/content/polygraph-city
[firearms]: https://opengameart.org/content/the-free-firearm-sound-library

Sound-design judgments: flight is an engine/wind-like whoosh, ice and telekinesis are distinct electronic power textures, and death is a short descending game-feedback tone rather than a recorded human vocal. Those choices are provisional until a human listens. The game’s existing cop attack is still contact-range damage, not a newly implemented firearm mechanic.

Rejected source: Tabasco's `Gunshot Sounds` page says CC0, but its `sounds.zip/creativecommons.txt` says CC-BY 3.0 (Vincent Sevedge). None of that archive is to be shipped. A separate, explicitly CC0 firearm source is used instead.
