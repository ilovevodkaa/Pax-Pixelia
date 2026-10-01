# Статус в Discord

Игра показывает друзьям в Discord «Играет в Pax Pixelia»: держава · эпоха и дата (или «Блиц недели»), время в игре,
логотип и значок эпохи. Код: `game/scripts/Core/Discord/` (`DiscordIpc` — локальный канал `discord-ipc-N` клиента
Discord, `DiscordPresence` — автозагрузка). Выключается в Настройки → Интерфейс → «Статус в Discord».

Приложение Discord: https://discord.com/developers/applications → **Pax Pixelia**; его Application ID записан в
`DiscordPresence.ClientId`. Картинки этой папки загружены туда в Rich Presence → Art Assets под своими именами:
`logo` (1024×1024, большая) и `era0` … `era10` (512×512, маленькая, по эпохе игрока).
