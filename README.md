# Ukeire

A Dalamud plugin that teaches Doman Mahjong. It analyses your hand, reviews your discards and calls, and gives hints when you ask for them.

Ukeire does not play for you. It does not click, discard or call. It reads only what the local player can see at the table: your own hand, the discard piles, the called melds and the round information. It never reads another player's concealed tiles.

## Status

This plugin is under development. Not every feature has been verified in game. Report anything that behaves incorrectly, and include the output of the Debug tab.

## Features

* Shanten and tile acceptance (ukeire) for the current hand. Both can be hidden in the settings.
* A ranked list of discards: lowest shanten first, then the most accepted tiles, then the most distinct tile kinds.
* Hints in three tiers: a nudge, a pointer and the full answer. A hint is shown only after you request it. The highest tier you allow yourself is set in the settings.
* A review of every discard and call you make, with a verdict and an explanation.
* Yaku guidance. When the hand is tenpai, each winning tile is evaluated exactly and shown with its yaku and han. Before that point, the plugin suggests yaku the hand can aim for. It warns when an open hand has no yaku in reach.
* Explanations of chi, pon and kan chances, with the risks, the benefits and the swap-call (kuikae) restriction.
* Riichi defense. While an opponent is in riichi, each tile in your hand is rated against them using their discards and the visible tiles. The ratings appear on the Table tab and in detail on the Defense tab.
* Warnings for furiten waits, liability (pao) for Big Three Dragons and Big Four Winds, and yakuman chances.
* A history of past games, saved per game and kept across plugin versions.
* A Learn tab with short lessons. It highlights the lessons relevant to the current hand.

Han is counted for yaku only. Dora, ura-dora, ippatsu, haitei, rinshan, chankan and fu are not counted. Among the yakuman, only Thirteen Orphans is detected in the exact evaluation.

## Usage

* `/ukeire` opens or closes the tutor window.
* `/ukeire config` opens the settings.

The window has six tabs:

| Tab | Contents |
| --- | --- |
| Table | Your hand, the analysis, call explanations, riichi defense, hints and yaku. |
| Defense | A safety rating for every tile in your hand against each player in riichi. |
| Review | The review of each discard and call. |
| History | Completed games and their statistics. |
| Learn | Lessons on shanten, yaku, defense and the rules specific to Doman Mahjong. |
| Debug | The raw values read from the game. Use it when reporting a problem. |

Open the settings and set the options you want before your first game. Disable the shanten display and the defense ratings if you want to practise reading them yourself.

### Tile design

The plugin draws tiles with the game's own icons. Set the Tile design option in the settings to Traditional, Doman or Alternate to choose a design. The "Match the game" option attempts to follow the design selected in the Mahjong settings of the game. That option is not reliable yet. Select a design manually if it draws the wrong one.

## Building

Requirements:

* XIVLauncher, FINAL FANTASY XIV and Dalamud installed, with the game started through Dalamud at least once.
* The .NET SDK required by `Dalamud.NET.Sdk` 15.0.0.
* Git, with access to the `FFXIVClientStructs` fork described below.

Steps:

1. Clone the repository with its submodule: `git clone --recurse-submodules <url>`. If you have already cloned it, run `git submodule update --init`.
2. Open `Ukeire.slnx` in your IDE, or run `dotnet build -c Release` in the `Ukeire` directory.
3. The plugin is written to `Ukeire/bin/Release/Ukeire.dll`.

The Mahjong structures are not part of the upstream `FFXIVClientStructs`. The project references a fork on the `feat/mahjong` branch as a submodule, and it does not use the copy bundled with Dalamud. The submodule URL uses SSH. Replace it with an HTTPS URL if you do not have an SSH key set up for GitHub.

## Installing

Ukeire is distributed through SheepGoMeh's custom plugin repository.

1. In the game, run `/xlsettings` and open the Experimental tab.
2. Under Custom Plugin Repositories, add this URL and save:
   `https://raw.githubusercontent.com/SheepGoMeh/PluginRepo/main/repo.json`
3. Run `/xlplugins`, search for Ukeire and install it.

Dalamud checks the repository for updates by itself. Remove the entry from the same list to stop receiving them.

### Installing a local build

Use this only if you are building the plugin yourself. Do not enable it together with the repository version.

1. Build the plugin as described above.
2. In the Experimental tab of `/xlsettings`, add the full path to `Ukeire.dll` to the Dev Plugin Locations list and save.
3. Run `/xlplugins`, open Dev Tools, then Installed Dev Plugins, and enable Ukeire.

The path only needs to be added once.

## License

This project is licensed under the GNU Affero General Public License, version 3. See `LICENSE.md`.

The Mahjong analysis was informed by [shinkuan/Akagi](https://github.com/shinkuan/Akagi) and [EndlessCheng/mahjong-helper](https://github.com/EndlessCheng/mahjong-helper). The algorithms were reimplemented. No code was copied.
