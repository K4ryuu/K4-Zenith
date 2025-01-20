<a name="readme-top"></a>

![GitHub tag (with filter)](https://img.shields.io/github/v/tag/K4ryuu/K4-Zenith?style=for-the-badge&label=Version)
![GitHub Repo stars](https://img.shields.io/github/stars/K4ryuu/K4-Zenith?style=for-the-badge)
![GitHub issues](https://img.shields.io/github/issues/K4ryuu/K4-Zenith?style=for-the-badge)
![GitHub](https://img.shields.io/github/license/K4ryuu/K4-Zenith?style=for-the-badge)
![GitHub all releases](https://img.shields.io/github/downloads/K4ryuu/K4-Zenith/total?style=for-the-badge)
![GitHub last commit (branch)](https://img.shields.io/github/last-commit/K4ryuu/K4-Zenith/dev?style=for-the-badge)

<!-- PROJECT LOGO -->
<br />
<div align="center">
  <a href="https://github.com/K4ryuu/K4-Zenith">
    <img src="https://i.imgur.com/sej1ZzD.png" alt="Logo" width="400" height="256">
  </a>
  <h3 align="center">CounterStrike2 | K4-Zenith</h3>
  <p align="center">
    K4-Zenith is a core plugin, that allow developers to create their own modules without having to struggle with database connections, player data, etc. It's a really easy to use plugin, with a lot of features and a lot of possibilities. Additionally we provide some official modules for Zenith, to empover your server with more features and high standards.
    <br />
    <a href="https://github.com/K4ryuu/K4-Zenith/releases">Download</a>
    ·
    <a href="https://github.com/K4ryuu/K4-Zenith/issues/new?assignees=K4ryuu&labels=bug&template=bug_report.md&title=%5BBUG%5D">Report Bug</a>
    ·
    <a href="https://github.com/K4ryuu/K4-Zenith/issues/new?assignees=K4ryuu&labels=enhancement&template=feature_request.md&title=%5BREQ%5D">Request Feature</a>
     ·
    <a href="https://kitsune-lab.com">Website</a>
     ·
    <a href="https://kitsune-lab.com/tickets/create?department_id=2">Hire Us</a>
  </p>
</div>

> [!WARNING]
> The plugin is still in development and may contain bugs. Please report any bugs you find in the [issues](https://github.com/K4ryuu/K4-Zenith/issues) section.

<!-- ABOUT THE PROJECT -->

## About The Project

K4-Zenith is a powerful plugin that simplifies the management of player data storage, command and config registration, and provides various features for developers. With K4-Zenith, you can easily handle MySQL player data storage, customize command and config structures, and ensure data validation. The plugin also includes an auto-update feature for configs, eliminating the need to manually update config keys. Additionally, K4-Zenith offers the ability to share config values between modules. It provides a globalized chat-processor, clantag manager, and priority-based actions. You can also register settings, which are added to a !settings menu for all palyers automatically without having to add MySQL or menu structures to your plugin. Using the API, you can leverage Zenith standards to create your own modules that seamlessly integrate with the Zenith core.

### Dependencies

To use this server addon, you'll need the following dependencies installed:

- [**CounterStrikeSharp**](https://github.com/roflmuffin/CounterStrikeSharp/releases): CounterStrikeSharp allows you to write server plugins in C# for Counter-Strike 2/Source2/CS2.
- **MySQL/MariaDB**: An up-to-date MySQL/MariaDB server is required to store player data, settings and use some of the modules aswell.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- ADDONS -->

### Official Modules

You can enhance your server with additional features and elevate its standards by utilizing our official modules. These modules are bundled in the Core Package, allowing you to selectively install only the ones you intend to use.

- **Zenith Stats**: This module provides a comprehensive player statistics system that tracks player statistics globally, as well as per map and per weapon. It includes intuitive menus to display the statistics to the players, allowing them to easily track their progress and performance.

- **Zenith Time Stats**: This module offers a player time statistics system that accurately tracks playtimes per team, per life state, and globally. It also includes a user-friendly menu to display the statistics to the player, along with playtime notifications to appreciate their dedication and time spent in the game.

- ⚠️ **Zenith Bans**: This module introduces a powerful admin system designed to simplify server management. It includes essential commands such as ban, kick, mute, gag, slay, slap, and more. Additionally, it provides a database-based player system that enables assigning ranks, permissions, immunity, groups, and even timed ranks like VIPs. With features like connect info, Discord webhooks, and more, this module offers a comprehensive set of tools to effectively manage your server.

- ⚠️ **Zenith Extended Commands**: This module adds a wide range of fun commands to your server, including commonly used admin commands like respawn, blind, revive, teleportation (tp), item giving (give), and much more. These commands enhance the gameplay experience and provide additional options for server administrators.

- **Zenith Custom Tags**: Enhance your server's chat experience with Zenith Custom Tags. This module gives players custom chat colors, ranks, clantags, and chat colors based on permissions or SteamID formats. Customize the chat environment for your community.

- **Zenith Toplists**: Display top players by given values. Supports Ranks, TimeStats and Stats module.

> [!WARNING]
> Do not install ⚠️ marked modules, if you are using SimpleAdmin or any similar system. This is because the the modules does the same as SimpleAdmin and can cause conflicts.

<!-- INSTALLATION -->

## Installation

To install the Zenith Core, follow these steps:

1. Download the latest [release](https://github.com/K4ryuu/K4-Zenith/releases/latest).
2. Extract the contents of the ZIP file to `counterstrikesharp/plugins`. `K4-Zenith` is required, the other are optional to install.
3. Start your server, which is going to generate the config files to `counterstrikesharp/configs/zenith`.
4. Modify the config files according to your preferences. Setup the MySQL connection and other settings that you want to customize.
5. Restart your server to apply the changes.
6. If Zenith finds old K4-System databases, it will automatically convert them to the new Zenith database structure. Follow the console instructions to complete the conversion.

> [!CAUTION]
> The core cannot be hotReloaded, so if you update files in `K4-Zenith` folder, you need to restart the server fully. The modules can be hotReloaded, so you can update them without restarting the server.

<!-- CORE COMMANDS -->

<!-- Panels -->

### Officially Supported Web Panels

These web based panels support Zenith data to be shown. Give some appreciation to them!

- [CSS-Bans](https://github.com/counterstrikesharp-panel/css-bans): CSS-BANS is an admin web panel for Counter-Strike 2, powered by CounterStrikeSharp. It allows for comprehensive management of bans, mutes, VIP statuses, skins, ranks, and more on your CS2 servers.
- [GameCMS](https://gamecms.org): Everything in one place for your dedicated game server!

### Core Commands

Most of the commands can be set in the configuration files, but here are some of the core commands that are available by default:

- **!settings**: Opens a menu with all settings that are registered in the Zenith Core.
- **!placeholderlist**: Lists all placeholders that are available in the Zenith Core. (Required permission @zenith/placeholders)
- **!zreload**: Reloads all configs that are registered in the Zenith Core. (Required permission @zenith/reload)
- **!commandlist**: Lists all commands that are registered in the Zenith Core with description and required permissions. (Required permission @zenith/commands)

<!-- PERMISSIONS -->

### Permissions

In Zenith, the root permission is designated as @zenith/root instead of @css/root. Please make sure to use @zenith/root for the root permission.

If you are unsure about which permission is required for a specific command, you can use the command `!commandlist` to retrieve a comprehensive list of all commands along with their corresponding required permissions.

For other permissions such as @zenith-bans/admin, which is necessary to access connect info and other features, you can refer to the value descriptions in the config files.

<!-- CONFIG -->

### Config

The Zenith Core config file is located in the `configs/zenith` folder. You can modify the config file to customize the settings according to your preferences. The config file includes various settings such as MySQL connection details, auto-update settings, and more.

These configuration files are made with YAML, which results in that we add descrptions, default values and more to the config files. This makes it easier for you to understand what each setting does and how to configure it without us having to create wiki pages for each setting.

> [!CAUTION]
> To avoid any issues, please only modify the `currentValue` in the config files and refrain from making any other changes.

<!-- ROADMAP -->

## Roadmap

- [ ] Optimise functions to call less var allocations
- [ ] Example developer files
- [ ] Wall toplists using placeholders from core
- [ ] Admin Menu
- [ ] Vote Manager module
- [ ] Game management system + connect messages
- [ ] Map management system
- [ ] Module to check last week / month / year playtime

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- AUTHORS -->

## Authors

- [**K4ryuu**](https://github.com/K4ryuu) - _Initial work_

See also the list of [contributors](https://github.com/K4ryuu/K4-Zenith/graphs/contributors) who participated in this project as an outside contributor.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- LICENSE -->

## License

Distributed under the GPL-3.0 License. See `LICENSE.md` for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- CONTACT -->

## Contact

- **KitsuneLab Team** - [contact@kitsune-lab.com](mailto:contact@kitsune-lab.com)

<p align="right">(<a href="#readme-top">back to top</a>)</p>
