# Universal Volume Controller

Client-side volume control for all items and other sounds within the game

Tired of loud noises? Tired of your friends blasting the air horn in your ears? TURN IT OFF!
Each individual item / sound can be lowered in volume or be fully muted.

## Features

- In-game menu toggle by pressing ```F10```
- Global volume slider with quick mute and unmute
- Per-category controls for:
  - Item Sounds
  - Environment
  - Other
- Per-sound sliders inside each category so you can fine-tune specific sounds
- Category-wide Mute All and Unmute All actions
- Search box to quickly find sounds by name
- UI customization with theme color presets and opacity control
- Automatic runtime discovery and refresh for newly seen audio sources
- Saves settings between sessions within the same game (SWITCHING TO A NEW GAME DOESNT KEEP THE SETTINGS)

### Debug Hotkeys

- ```F9``` + ```F10```: Dump active audio sources to the log
- ```F9``` + ```F10``` + ```LeftCtrl```: Dump all audio sources (including non-playing)

### Notes

- This mod is designed for player-side control and does not synchronize volume values to other players.
- Some audio tracks don't seem to lower in volume, but that's because it is barely noticable sometimes. When dropping it beneath 50% you'll most likely start to hear the difference. I might tweak that in a later version.

### Known Issues

- Some audio tracks are not in their respective tab yet
