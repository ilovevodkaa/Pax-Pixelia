# Pax Pixelia — звук (v1)

Закон для агентов, которые подключают звук: интерфейс, карту, события и музыку. Документ продолжает решение [MAIN_MENU.md §1.5](MAIN_MENU.md): Kenney CC0 на интерфейсе, музыки пока нет.

**Откуда данные.** Четыре поисковых агента (UI, атмосфера, стингеры, музыка) нашли 66 кандидатов. Проверка лицензий дала такой итог: 45 «OK», 17 «OK с атрибуцией», 4 «НЕЯСНО», 0 «НЕЛЬЗЯ». Имена файлов в бандлах Sonniss GDC я дополнительно сверил по листингу папок. Отсюда и точные имена и размеры в §5.

**Ограничения.** Ничего не скачано и не куплено, ни в какие аккаунты никто не входил. Звук никто из агентов не слушал: оценки основаны на репутации авторов, рейтингах, составе паков, способе записи и формате. Прежде чем одобрять загрузку, послушайте превью по порядку из §5.0.

Обозначения лицензий: **OK** — можно в коммерческой игре без условий; **OK+А** — можно, но строка в «Авторах» обязательна.

---


> **Решения автора (27.09.2026):**
> 1. Скачан лёгкий бесплатный набор Kenney (Interface Sounds, UI Audio, RPG Audio, Impact Sounds, Music Jingles; CC0) → `audio_src/kenney/` (вне git), отобранные файлы пойдут в `game/assets/audio/`. BigSoundBank закрыт антибот-проверкой — машинку автор скачает вручную при желании, пока щелчки Kenney.
> 2. **Атмосферу (GDC, Freesound) пока не делаем.**
> 3. **Музыка — лейтмотив + музыка по 5 группам эпох** (GDD обновлён).
> 4. **Покупки откладываем**: до плейтеста бесплатные замены.
> 5. Репозиторий публичный: звуки с лицензиями, запрещающими распространять файлы (GDC, платные), никогда не коммитим.

## 1. Звуковое направление

1. **Звук повторяет картинку.** Интерфейс монохромный, и звучит он так же «бесцветно»: короткие сухие щелчки материала (пластик, дерево, бумага, металл). Мелодий и чиптюнового писка в частых звуках нет. Цвет живёт на карте и в событиях: там появляются тембр, природа и инструменты. Мелодия есть только у стингеров и музыки.
2. **Карта — это настольная диорама, а не кино.** Клик по провинции звучит как деревянная фишка на доске, окно события как пергамент, золото как монеты в руке. Фоны тихие, без узнаваемой речи, с лёгким срезом верхов. Микс зависит от зума:
   - на дальнем плане слышны только ветер, море и общий «шум мира», low-pass около 8–10 кГц;
   - на ближнем добавляются птицы, вода, слой цивилизации и точечные звуки;
   - точечные звуки в моно, панорама по положению на экране.
3. **Палитра по эпохам.** Частые звуки (наведение, клик, вкладки) не меняются всю игру: к ним привыкает рука. От эпохи зависят три вещи: материал характерных событий, слой цивилизации на карте и инструмент лейтмотива. 11 эпох сведены в 5 звуковых групп:

   | Группа | Эпохи | Материал событий UI | Слой цивилизации на карте | Инструмент лейтмотива | Музыка |
   |---|---|---|---|---|---|
   | **Г1 Костёр** | Первобытная | дерево, камень, кость | костёр, ветер, звери | рамочные барабаны, костяная флейта | нет (см. п. 7) |
   | **Г2 Глина** | Древний мир, Античность | дерево, пергамент, глина | деревня, рынок-сук, караван | рог, лира или арфа | восток, мягкий оркестр |
   | **Г3 Перо** | Средневековье, Возрождение | пергамент, перо, металлическая защёлка | колокола, базар, пастушьи колокольцы, гавань | орган с колоколами → клавесин | лютня, северный эпос, ранняя классика |
   | **Г4 Латунь** | Эпоха пара, Индустриальная, Атомная | латунь, часы, печатная машинка | котельная, цех, повозки по камню | духовой оркестр → полный оркестр | классика XIX века, индустриальный оркестр |
   | **Г5 Сигнал** | Информационная, Космическая, Будущее | стекло, электроника | тихий гул, почти тишина | синтезатор, кивок чиптюном | эмбиент, электроника |

4. **Громкость и длина.** Одна общая нормализация на все источники, иначе разные паки «прыгают».

   | Слой | Уровень | Длина |
   |---|---|---|
   | Наведение | на 6–9 дБ тише клика, срез низов (HPF) 120–150 Гц | до 80 мс |
   | Клик, тумблер, вкладка | опорный уровень UI | до 150 мс |
   | Панели, книга, бумага | клик ±3 дБ | 0,2–0,6 с |
   | Фишка на карте | клик +2 дБ | до 250 мс |
   | Точечные звуки карты | на 6–10 дБ громче подложки | до 2 с |
   | Подложки биомов и цивилизации | −23…−20 LUFS | петля 60–120 с |
   | Стингеры | около −16 LUFS, пик −1 dBTP | 0,8–3 с; эпоха и чудо до 6 с |
   | Master | лимитер, потолок −1 dBTP | — |

5. **Вариации и тон.** У каждого частого события 2–8 вариантов (`name_N`), один и тот же вариант подряд не играет. Тон случайный в пределах ±3–5 %. Минимальный интервал уже есть в `Sfx` (hover 45 мс, tick 28 мс). Шаги зума меняют тон по направлению: на приближении выше, на отдалении ниже. Фишка на карте получает ±2 % тона и ±1,5 дБ громкости, иначе при сотне кликов за партию слышен «пулемёт».
6. **Под события мир притихает, стингеры идут в очередь.** Пока звучит стингер, атмосфера опускается на 4–6 дБ, музыка на 6 дБ, а через 1,5 с всё возвращается. Не больше одного стингера за 1,5–2 с, остальные ждут или сливаются. Приоритет в мирном MVP: бедствие → смена эпохи → чудо → религия → открытие → остальное. Во втором этапе война встаёт перед бедствием.
7. **Тишина — тоже инструмент.**
   - Меню открывается в тишине: это уже решено.
   - Между музыкальными треками 30–90 с одной атмосферы, как в Civilization.
   - На паузе мир «замирает»: атмосфера уходит за low-pass около 1,2 кГц и на −6 дБ.
   - Перед фанфарой новой эпохи 0,4 с почти полной тишины, как вдох.
   - **Предложение:** в Первобытной эпохе музыки нет совсем, только мир, костёр и барабанные стингеры. Музыка рождается вместе с цивилизацией.
8. **Стингеры растут вместе с эпохой.** У игры один лейтмотив «От костра до звёзд» из 4–5 нот. Он звучит в меню, при смене эпохи и на финале. Мелодия не меняется, меняется инструмент (см. таблицу в п. 3): игрок буквально слышит пройденный путь. Пока своего мотива нет, смену эпохи озвучивает лестница инструментов Ovani (§2.5).

---

## 2. Рекомендованный набор («берём»)

Только лицензии «OK» и «OK+А». В каждой категории один-два главных автора, чтобы звук был цельным.

### 2.0 Сводка

| Категория | Главные голоса | Цена | Обязательная атрибуция |
|---|---|---|---|
| Интерфейс | **Kenney** (основа) + **JDSherbert** (фишки на карте) + **Leohpaz** (книга и пергамент) | ≈ $8,5 | JDSherbert |
| Атмосфера карты | **Ivo Vicic** (природа, GDC) + **Felix Blume** (цивилизация, CC0) | 0 | YleArkisto (один файл каравана) |
| Стингеры | **Ovani Sound** Jingles & Stingers Vol. 1–2 + свой лейтмотив | $40 | нет |
| Музыка (после решения по GDD) | **Ovani Sound** + **Musopen** (CC0) + свой лейтмотив | по паку ≈ $50 | нет |

### 2.1 Интерфейс

| Что | Автор, где послушать | Лицензия | Атрибуция | Для чего | Почему «отлично» |
|---|---|---|---|---|---|
| **Interface Sounds** — 100 OGG | Kenney · [kenney.nl/assets/interface-sounds](https://kenney.nl/assets/interface-sounds) | OK · [CC0](https://creativecommons.org/publicdomain/zero/1.0/) | не нужна | основа: click, select, tick, scroll, open/close, maximize/minimize, error, confirmation, glass/pluck (тосты), bong (важное), question, drop | «Взрослые» короткие звуки без мультяшности, громкость выровнена, по 2–9 вариантов на группу. Часть уже в проекте, общий стиль с Mr. President |
| **UI Audio** — 50 OGG | Kenney · [kenney.nl/assets/ui-audio](https://kenney.nl/assets/ui-audio) | OK · CC0 | не нужна | тумблеры, вкладки, скорость времени, rollover на наведение | Записи настоящих выключателей: тактильно и сухо. Тот же мастеринг, что у Interface Sounds |
| **RPG Audio** — 50 OGG | Kenney · [kenney.nl/assets/rpg-audio](https://kenney.nl/assets/rpg-audio) | OK · CC0 | не нужна | handleCoins на мелкое золото, metalLatch на паузу, cloth на знамя; bookOpen/bookFlip как бесплатная замена Leohpaz | Тот же автор и мастеринг, смешивается без правок |
| **Tabletop Games SFX Pack** — 56 звуков, 280 файлов, WAV 48/24 | JDSherbert · [jdsherbert.itch.io/tabletop-games-sfx-pack](https://jdsherbert.itch.io/tabletop-games-sfx-pack) | OK+А · [условия автора](https://jdsherbert.itch.io/terms-and-conditions) | **обязательна:** «Sounds by JDSherbert – https://jdsherbert.itch.io» | **фирменный звук карты:** фишка на выбор провинции, постановку разведчика, основание города; бумага на карточки событий; кубики на пасхалки | Сразу даёт ощущение «настольной стратегии», которого нет у стандартных кликов. Студийные 48/24, много вариаций, 4.9 (13 оценок). £4,99; есть бесплатная урезанная версия, начать с неё |
| **Book/Parchment UI SFX** — 22 звука | Leohpaz · [leohpaz.itch.io/22-bookparchment-ui-sfx](https://leohpaz.itch.io/22-bookparchment-ui-sfx) | OK · лицензия на странице (коммерция разрешена, пак не раздавать) | по желанию: «SFX: Leohpaz» | летопись, энциклопедия, окно события, дерево технологий: открыть, закрыть, перелистнуть, подтвердить решение | Исторический «бумажный» характер без музыки. Автор держит 5.0 по всему каталогу, «No generative AI». $1,99 |
| **Typewriter: Key, space, Bell #1/#2, #3, #4** (Hermes Precisa 305) | Joseph Sardin · BigSoundBank: [клавиша](https://bigsoundbank.com/typewriter-key-s2842.html), [пробел](https://bigsoundbank.com/typewriter-space-s2843.html), [звонок 1](https://bigsoundbank.com/typewriter-bell-s2844.html), [звонок 2](https://bigsoundbank.com/typewriter-bell-s2845.html), [набор #3](https://bigsoundbank.com/typewriter-3-s2836.html), [набор #4](https://bigsoundbank.com/typewriter-4-s2837.html) | OK · [CC0](https://bigsoundbank.com/licenses.html) | по желанию: «Joseph SARDIN — BigSoundBank.com» | буквы хроники в Г4: удар на каждые 2–3 символа, звонок каретки в конце записи; телеграммы и донесения | Одна машинка, хороший микрофон (Neumann KM184), WAV 48/24, поэтому удары однородны. Брать только s2835–s2845: «Typewriter #1» (s1065) — другая машинка и другая запись |
| **Выборка из Sonniss #GameAudioGDC** (точные файлы в §5) | CB Sound Design (Board Games, Pencils, Coins), 344 Audio (Ultimate Chess), The Soundcatcher (Paperlife), Hzandbits (Money), Shapeforms (The Mint), Justsoundeffects (Clocks) · [sonniss.com/gameaudiogdc](https://sonniss.com/gameaudiogdc/) | OK · [GDC Bundle License](https://sonniss.com/gdc-bundle-license/) | не нужна | перо и карандаш для хроники в Г1–Г3, пергамент, крупное золото (сделки, казна), тиканье и завод часов на скорость времени в Г4, пробные фишки на доске | Профессиональные записи бесплатно и без атрибуции. Закрывают «сюжетные» звуки: перо, пергамент, часы, монеты |

### 2.2 Интерфейс: раскладка по событиям

Имена — это ключи `Sfx.Play(...)`, файлы `name_N.ogg` / `name_N.wav`.

| Ключ `Sfx` | Событие | Источник | Заметка |
|---|---|---|---|
| `hover` | наведение | UI Audio rollover, Interface select | тише клика на 6–9 дБ, HPF 150 Гц |
| `click` | нажатие | Interface click ×5 | как сейчас |
| `toggle_on` / `toggle_off` | тумблер | UI Audio switch, пары | «вкл» на полтона выше |
| `tab` | вкладка | Interface switch ×7 | |
| `tick` / `scroll` | ползунок, шаг зума, прокрутка | Interface tick ×3, scroll ×5 | тон зависит от направления зума |
| `open` / `close` | малые панели, тултипы-окна | Interface open/close, maximize/minimize | как сейчас |
| `book_open` / `book_close` / `page` | летопись, энциклопедия, окно события, дерево технологий | Leohpaz Book/Parchment (временно Kenney RPG book*) | |
| `confirm` / `error` / `question` | решение, отказ, диалог выбора | Interface confirmation ×4, error ×8, question ×4 | |
| `toast` / `toast_important` | уведомление обычное / важное | Interface glass ×6, pluck ×2 / bong | важные выше в очереди |
| `piece` | выбор провинции | JDSherbert Tabletop, фишка (проба: GDC CB `medium_wooden_pieces_on_cardboard_7`) | ±2 % тона, ±1,5 дБ |
| `place` | основать город, поставить разведчика | Tabletop, удар фишки, Interface drop | |
| `speed_1..3` | скорость времени | UI Audio switch от лёгкого к тяжёлому; в Г4 JSE pocket watch winding | |
| `pause` / `unpause` | пауза | Kenney RPG metalLatch; в Г4 тиканье JSE, которое обрывается | плюс low-pass атмосферы |
| `type` / `type_space` / `type_bell` | буквы хроники | Г1–Г3: перо (CB Pencils, 344 Chess «Writing With Pencil 04»); Г4–Г5: Hermes s2842/s2843/s2844 | нарезать 6–10 ударов |
| `coin` / `coins` | мелкое золото / сделка, казна | Kenney RPG handleCoins / GDC CB Coins, Shapeforms Mint, Hzandbits Money | |
| `paper` | карточка события, торговое предложение, карта от разведчика | GDC Soundcatcher `Paper_Pergament_Pages_Turn_Flip_20`, `Paper_Rustling_Movement_Handling_50`; Tabletop, бумага | |
| `stamp`, `whoosh` | штамп заголовка, шторка | синтез в `Sfx` (уже есть) | |

**Эпохальные «кожи».** Кожа — это таблица переназначения ключей по группе эпох. Меняются только `confirm`, `page`, `type`, `pause`, `speed`, `paper`: дерево и пергамент → перо и защёлка → машинка и часы → стекло (Г5 — волна 3, см. Shapeforms Free SFX в §3).

### 2.3 Карта: подложки по биомам

Главный голос — **Ivo Vicic** (Хорватия, полевые записи 24/96 из Sonniss GDC; превью: [SoundCloud](https://soundcloud.com/ivo-vicic/european-mountain-forest-animals-and-soundscapes-fx-library-preview), [ivovicic.bandcamp.com](https://ivovicic.bandcamp.com)). Пробелы закрывают Hzandbits, Discover Oregon, Faunethic и InspectorJ (тоже GDC) и Felix Blume (CC0). Все подложки ниже — **OK**, атрибуция не нужна.

| Биомы (GDD §9.3) | Подложка | Источник |
|---|---|---|
| Океан, мелководье, риф, фьорд | `08 Small beach_gentle waves_echo 1` (дальний план), `59 Beach_wave_near 6` (ближний) | Ivo Vicic · Calm sea · GDC 2019 |
| Река, озеро | `74 River_mid field_LOOP 2`, `63 River_near field_LOOP 11` (уже петли) | Ivo Vicic · Mountain rivers and streams · GDC 2020 |
| Равнина, луг, степь, саванна | `Wind,Ext,Gust,Grasslands,Rustle,Buffeting` | Hzandbits · Wind In Trees · GDC 2017 ([превью](https://sonniss.com/sound-effects/wind-trees/)) |
| Холмы, горы | `38 Wind in grass_Velebit mountain` | Ivo Vicic · Natural ambiences vol.1 · GDC 2019 |
| Лес | `14 Wind_dead leaves_whoosh_forest_birds` | Ivo Vicic · Bora wind · GDC 2018 |
| Тайга | `58 Wind in pines_mid field_MS stereo` | Ivo Vicic · Winter textures vol.1 · GDC 2019 |
| Тундра | `91 Wind_winter grass_2_MS stereo` | там же |
| Снег, ледник | `07 Blizzard_Snow melting_wind_MS stereo`; порывы `Wind Gusts 2016 Lookout Snow - Biggest Gusts Long` | Ivo Vicic · Winter textures; Discover Oregon · Wind and Storms · GDC 2018 |
| Джунгли | `Jungle quiet insects and birds wide _120407_11` | Faunethic · Thailand · GDC 2017 |
| Болото | `Wind,Ext,Breeze,Reeds,Rustle,Howl,Whip,Whistle` + `Rain 2014 Frogs Rain` | Hzandbits · GDC 2017; Discover Oregon · GDC 2018 |
| Пустыня | «Wind blowing into some cactus spine… Atacama» — 210 оценок, 11,7 тыс. скачиваний | Felix Blume · [freesound.org/s/156414](https://freesound.org/people/felix.blume/sounds/156414/) · CC0 |
| Оазис, каньон | `Desert canyon quiet, light birds and wind, large reverb_160821_006` | Faunethic · Jordan · GDC 2017 |
| Вулкан | `Lava Lava Windy`, треск `Lava Lava Short Windy Crackles` | Discover Oregon · Lava from the Kilauea Volcano · GDC 2018 |
| Дождь (погода) | `RAINVege_InsJ_Ambience_Rain_Moderate_02_LOOP` (уже петля); `92 Rain_mountain_cabin_porch` | InspectorJ · Essentials 06 Rain · GDC 2021-23; Ivo Vicic · Rain · GDC 2020 |
| Гроза | 3 удара `THUN_InsJ_Thunder_*` поверх дождя; фон `185 Soundscape_mountain forest_thunderstorm…` | InspectorJ · Essentials 01 Thunder; Ivo Vicic · GDC 2020 |
| Шторм на море | `03 Wind_grass_ strong_hurricane blast 3` | Ivo Vicic · Bora wind · GDC 2018 |

**Почему это «отлично».** Почти все природные фоны записал один человек одним комплектом (24/96, по описанию без самолётов и трасс), поэтому биомы звучат как один мир. Часть файлов уже нарезана петлями. Лицензия GDC самая удобная: коммерция без атрибуции.

### 2.4 Карта: слой цивилизации и точечные звуки

| Что | Эпохи | Источник | Лицензия |
|---|---|---|---|
| Костёр племени (петля у лагеря и столицы) | Г1 | Spandau · «campfire.wav» · [freesound.org/s/40699](https://freesound.org/people/Spandau/sounds/40699/) (24/48, 146 оценок, чистый треск без обработки) | OK · CC0 |
| Деревня | Г2 | Faunethic Jordan · `Village medium in a canyon voices activity and horses` · GDC 2017 | OK · GDC |
| Рынок древнего города | Г2 | Felix Blume · «Market in Africa… Diafarabé (Mali)» · [freesound.org/s/173153](https://freesound.org/people/felix.blume/sounds/173153/); Faunethic Jordan · `Market souk medium voices…` | OK · CC0 / GDC |
| Далёкий город на среднем зуме (по желанию) | Г2–Г3 | Felix Blume · «Souk ambience far away with slight wind and donkey» · [freesound.org/s/779853](https://freesound.org/people/felix.blume/sounds/779853/) | OK · CC0 |
| Колокола средневекового города | Г3 | Felix Blume · «Church Bells at noon on Sunday (Prague)» · [freesound.org/s/462474](https://freesound.org/people/felix.blume/sounds/462474/) (96/24) | OK · CC0 |
| Пастбище, пастушьи колокольцы | Г2–Г3 | Felix Blume · «Sheep in a field… Bask Country» · [freesound.org/s/138424](https://freesound.org/people/felix.blume/sounds/138424/) | OK · CC0 |
| Праздник, звонари (город растёт, основание) | Г2–Г3 | Ivo Vicic · Bellmen · `22 BELLMEN_Kukuljanski Bell ringers` · GDC 2021-23 | OK · GDC |
| Гавань | Г3–Г4 | Ivo Vicic · Northern Mediterranean · `157 Urban_island_small town_harbour_water lapping…` · GDC 2021-23 | OK · GDC |
| Котельная, гул пара | Г4 | Ivo Vicic · Industrial gas boiler room · `23 …room tone_boiler near_PAN C` · GDC 2021-23 | OK · GDC |
| Цех | Г4 | Mononeshot · Industrial, Ironworks · `10 - Machining shop - Ambient noise V2` · GDC 2021-23 | OK · GDC |
| **Караван** (на торговом пути вблизи) | Г2–Г3 | YleArkisto · «Niger, Tuareg market, people, camels, bells, buzz» (1989) · [freesound.org/s/254668](https://freesound.org/people/YleArkisto/sounds/254668/) | **OK+А** · CC BY 4.0 |
| Повозка по камню, лошадь | Г3–Г4 | Dramatic Cat · Horse Carriage · `VEHWagn_Wood Cart Roll On Stone Pavement…`, `FEETHors_Draft Horse Walk On On Grass And Dirt MONO` · GDC 2021-23 | OK · GDC |
| Шаги разведчика по биому | все | Kenney · [Impact Sounds](https://kenney.nl/assets/impact-sounds): footstep_grass / snow / concrete / wood по 5 вариантов | OK · CC0 |
| Стройка | все | Kenney Impact Sounds: impactMining, impactPlank; основание в Г1 — треск `sm-fire-firewood-small-03-DPAmono` (The Sound Keeper, GDC 2018) | OK |

Караван от финского вещателя Yle — лучший найденный звук: верблюды, колокольцы и торг в одной записи. Ради него одна строка CC BY в титрах оправдана. Слой Г5 в MVP не нужен; в §3 есть запасные поздние города и космические гулы.

### 2.5 Стингеры событий

Главный голос — **Ovani Sound, Jingles & Stingers Vol. 1 и Vol. 2** (**OK**, [условия](https://ovanisound.com/policies/terms-of-service), атрибуция по желанию: «Sound effects by Ovani Sound»; $20 за том). Один автор, одинаковое сведение, «100% human made». Группы Vol. 1 почти один в один совпадают с нашими событиями, а Vol. 2 содержит готовую лестницу инструментов по эпохам. Превью: [Vol. 1](https://www.youtube.com/watch?v=Xdv1hWQ_bu8), [Vol. 2](https://www.youtube.com/watch?v=hDKUiETLRt0).

| Событие (мирный MVP) | Звук | Слой под ним |
|---|---|---|
| **Смена эпохи** | цель — свой лейтмотив (п. 8, §6.5); пока Ovani V2 по группам: Г1 Drums → Г2 Horn → Г3 Bells, потом Orchestral → Г4 Steampunk и Orchestral → Г5 Keys, Synth | 0,4 с тишины до стингера |
| Открыта технология | Ovani V1 Discovery | Leohpaz `page` |
| Окно события | Ovani V1 Mysterious или Update | Leohpaz `book_open` |
| Встречен новый народ | Ovani V1 Signal | Г1–Г2: короткий фрагмент `23 BELLMEN_Traditional horns` (Ivo Vicic, GDC) |
| Разведчики открыли земли | Ovani V1 Discovery, короткий | `paper` (пергамент) |
| Основана религия | «Choir of Voices – Single Chord», bone666138 · [freesound.org/s/274121](https://freesound.org/people/bone666138/sounds/274121/) · CC0 (одна певица записала трёхголосный аккорд в наложении, 124 оценки) | Ovani V2 Bells |
| Чудо построено | Ovani V2 Orchestral, длинный | Г4–Г5: `FRWKComr_InsJ_Fireworks-Display_Multi_02-01` (InspectorJ, GDC) |
| Стройка завершена | Ovani V1 Update, тихо | Kenney impactPlank |
| Достижение | Ovani V1 Positive | — |
| Торговая сделка | `coins` + `confirm` | — |
| Казна пустеет, голод | Ovani V1 Negative | Kenney handleCoins, медленнее |
| Чума, смерть правителя | «funeral bell – cloche funèbre», aoristos · [freesound.org/s/329324](https://freesound.org/people/aoristos/sounds/329324/) · CC0: 1–2 удара | Ovani V1 Negative |
| Извержение | `Lava Lava Short Windy Crackles` + Ovani V1 Negative | низкий `bong` |

**Лейтмотив своими силами.** Мотив сочиняем сами и рендерим 11 раз на CC0-сэмплах **VSCO 2 Community Edition + VCSL** (Versilian Studios, [versilian-studios.com/vsco-community](https://versilian-studios.com/vsco-community/), [github.com/sgossner/VCSL](https://github.com/sgossner/VCSL); **OK**, CC0). Синтезатор и чиптюн для Г5 делает наш `Synth.cs`. Только так фанфара будет одной мелодией во всех эпохах. Ни один готовый пак этого не даёт.

**Бесплатная заглушка до покупки:** Kenney [Music Jingles](https://kenney.nl/assets/music-jingles) (CC0): Pizzicato на открытия, Hit на достижения, Retro на пасхалки. Saxophone и Steeldrum не берём: мультяшно.

### 2.6 Пасхалки

Шутки озвучиваем живыми инструментами и голосами, без мультяшных «боингов», тогда юмор остаётся историческим.

| Пасхалка | Звук | Лицензия |
|---|---|---|
| Танцевальная чума (Страсбург, 1518) | «A Lover and His Lass» (Морли, около 1600), лютня и флейта · soundsandrebounds · [freesound.org/s/769807](https://freesound.org/people/soundsandrebounds/sounds/769807/): 4–6 с куплета | OK · CC0 |
| «Сделай перерыв» (реальная сессия слишком длинная) | зевок · benniknop · [freesound.org/s/317845](https://freesound.org/people/benniknop/sounds/317845/) (MKH 416): вырезать 2–3 с | OK · CC0 |
| 8-битный кивок, секретные события | Kenney Music Jingles, Retro (NES) | OK · CC0 |
| Кубики судьбы в случайных событиях | JDSherbert Tabletop, dice | OK+А |

Война с эму появится только во втором этапе (нужна война). Рецепт на будущее: GDC «344 Audio – Geese» с пониженным тоном, затем очередь «Super Thump – Weapons of World War II», затем грустный тромбон.

### 2.7 Музыка

> **Конфликт с GDD.** В [GDD.md §9.2](../GDD.md) записано: «UI и музыка общие для всех эпох». Музыка по группам эпох этому противоречит, решать автору (§7, вопрос 1). Предлагаю компромисс: один общий лейтмотив (меню и фанфары эпох) плюс плейлисты, окрашенные группой эпох. Если GDD остаётся как есть, играет один общий плейлист, а эпоху слышно только в стингерах.

Направление — спокойная акустика и оркестр с инструментами эпохи, в духе мирных треков Civilization IV/VI, Old World и HOI4. Чиптюн на роль фона не подходит: утомляет за часы игры и спорит с серьёзным тоном. Его оставляем для пасхалок.

| Что | Автор, где послушать | Лицензия | Для чего | Почему «отлично» |
|---|---|---|---|---|
| **Тема меню и фанфары эпох** | свой лейтмотив на VSCO 2 CE + VCSL, к релизу можно заказать у композитора | OK · CC0 (сэмплы) | меню, смена эпохи, финал | Единственный способ получить одну мелодию «от костра до звёзд»; права полностью наши |
| **Ovani Sound — музыкальные паки** (Orchestral Ambient 1–4, Classical 1–3, Eastern, Nordic, Industrial, Ambient, Electronic Ambient) | [ovanisound.com](https://ovanisound.com/products/orchestral-ambient-music-pack-vol-1): на каждой странице плейлист SoundCloud | OK · [Ovani FAQ](https://ovanisound.com/pages/faq): коммерция, без атрибуции, файлы только внутри игры | основной фон партии: Eastern для Г2, Nordic и Classical для Г3, Industrial для Г4, Ambient и Electronic Ambient для Г5, Orchestral Ambient как общий фон | Настоящая адаптивность: у каждого трека 3 уровня интенсивности. Одна студия, WAV 48/24, «NO AI». Тот же автор, что у стингеров. Около $50 за пак, Ovani регулярно бывает в Humble Bundle |
| **Musopen — записи классики** | [archive.org/details/musopen-lossless-dvd](https://archive.org/details/musopen-lossless-dvd), [Kickstarter Recordings Lossless](https://archive.org/details/MusopenKickstarterRecordingsLossless) | OK · Public Domain Mark / CC0 | Г4 по образцу Civ IV: медленные части Брамса, Бетховена, Грига, Мендельсона, Шуберта | Настоящий оркестр при нулевом риске по лицензии. Брать только эти две коллекции с archive.org: на самом musopen.org есть записи под CC BY-SA |
| **FREE Music Loop Bundle** (200+ петель) | Abstraction / Tallbeard · [tallbeard.itch.io/music-loop-bundle](https://tallbeard.itch.io/music-loop-bundle) | OK · CC0 | загрузка, пауза, резерв для Г5 | 4.9 (321 оценка), CC0 и без Content ID |

Музыка не входит в текущую волну: меню пока звучит тишиной (MAIN_MENU.md §1.5). Первобытная эпоха по предложению из §1 п. 7 обходится без музыки.

---

## 3. Запасные варианты

| Вариант | Лицензия | Когда брать |
|---|---|---|
| JDSherbert Wooden UI (£2,49) и Ultimate UI (£4,99, 5.0 при 34 оценках) | OK+А | деревянная «кожа» для Г1–Г2; много вариантов курсора |
| Leohpaz Retro RPG 100 UI ($3,49) | OK | отдельные звуки Pause/Unpause, Buy/Sell, Save Game |
| Shapeforms Free SFX (180 звуков, 4.9 при 67 оценках) и The Mint Lite ($4,99) | OK | Future UI для «кожи» Г5; полная библиотека монет с петлями счётчика казны |
| ObsydianX Interface SFX Pack 1 | OK · CC0 | тональные подтверждения; брать 1–2 самых сдержанных стиля |
| Kenney Casino Audio | OK · CC0 | фишки (chipsStack) на «весомое» золото, карты на события |
| Cyrex / Nathan Gibson Universal UI (Wood Block, Minimalist) | OK+А · CC BY 4.0 | альтернативные клики ранних эпох; после звуков обрезать тишину |
| Ovani Sound FX Starter Pack Vol. 1 (Godot Asset Store, бесплатно) | OK | послушать стиль Ovani до покупки; Medieval и Steampunk как акценты |
| Lokif GUI Sound Effects (OGA), Breviceps Clicks (Freesound) | OK · CC0 | закрыть случайные пробелы |
| InspectorJ на Freesound: Seaside Waves, Night Wildlife, Rain | OK+А · CC BY 4.0 | если побережья Ivo Vicic окажется мало |
| klankbeeld: пачки колоколов и лесов (Freesound) | OK+А · CC BY 4.0, у части файлов NC | больше колоколов для Г3; лицензию смотреть на каждом файле |
| Benboncan «Large Anvil & Steel Hammer 4», «Steam Train» | OK+А · CC BY 4.0 | кузница и паровоз для Г4 |
| Faunethic Turkey `Market big bazard indoor…`, Thailand `Forest cicadas…at night` | OK · GDC 2017 | базар Г3; тропическая ночь |
| Stefano Cremona `Campfire.wav`, `Crickets.wav` | OK · GDC 2021-23 | второй костёр, ночь |
| Dramatic Cat Olivetti Linea 98 | OK · GDC 2021-23 | вторая машинка для Г5 |
| West Wolf Protesting People, TheWorkRoom South African Soccer Crowds | OK · GDC | бунт и ликование: после MVP; вырезать речь и свистки |
| Pole Position Cities, 344 Audio UK Big City, CB Exoplanets, Systematic Sound High Tech Soul | OK · GDC 2021-23 | города и космос для Г5 |
| Soundholder Harbours Of Norway | OK · GDC 2019 | северная гавань; в файле слышен мотор |
| InspectorJ RPG Orchestral Essentials (Music FX), $49,99 на Sonniss | OK (лицензия на одного пользователя) | одна тема на 11 инструментах, если своего мотива не будет |
| Ovani Jingles & Stingers Vol. 3 ($20) | OK | стингеры с восточной, западной и электронной окраской по культурам |
| Leohpaz RPG Jingles and Fanfares ($3,49) | OK | «пиксельный» слой стингеров, если оркестр покажется тяжёлым |
| DeVern «Distant War Horn» (CC0) и GDC «344 Audio – Medieval Battle» | OK | война, второй этап |
| Epic Stock Media Synthesized Nature Loops ($36,75) | OK | готовые петли биомов; на itch 2.0 при 1 оценке, сначала послушать |
| Музыка CC BY: Scott Buckley, Kevin MacLeod, Alexander Nakarada, Chris Zabriskie | OK+А | у всех риск Content ID: стримеры поймают claim. Только с «режимом стримера» или платной лицензией. MacLeod ещё и слишком узнаваем |
| Музыка CC BY без Content ID: Alexandr Zhelanov (OGA), SubspaceAudio JRPG Pack 4 Calm (CC0) | OK+А / OK | средневековые темы; чиптюн для пасхалок |
| alkakrab Mega Bundle (€14,99, 610+ треков), Rodrigo Flores War & Diplomacy (CC BY) | OK / OK+А | дешёвое заполнение плейлистов; музыка войны (второй этап). У alkakrab до релиза прочитать PDF лицензии |
| Eric Matyas, soundimage.org | OK+А (кредит внутри игры) | короткие сцены: энциклопедия, экран чуда |

---

## 4. Отклонено

| Что | Причина |
|---|---|
| BBC Sound Effects (RemArc) | лицензия только для личного и учебного использования |
| Любые CC BY-NC (Robinhood76, Blue Dot Sessions, NC-файлы Felix Blume и klankbeeld), записи эму с xeno-canto | некоммерческая лицензия |
| Wikimedia Commons | решение проекта (MAIN_MENU §1.5), часто CC BY-SA |
| Саундтрек 0 A.D. | CC BY-SA 3.0: ShareAlike может распространиться на игру |
| undefined21 «Minimalist UI», mazarelli «Ancient Echoes» | сгенерировано ИИ (ElevenLabs, «AI Assisted») |
| Pixabay, неизвестные itch-паки «100 ambient loops» | авторство не проверяется, встречаются перезаливы и ИИ |
| SND (snd.dev) | запрещено использование, «связанное с политикой или религией», а в игре есть и то и другое |
| Google Material sounds | официальная страница удалена; зеркало даёт противоречивую лицензию |
| ZapSplat, звуки soundimage.org | бесплатно только MP3 (задержка на старте), нужны аккаунт и атрибуция |
| Epidemic Sound | подписка |
| Tabletop Audio | лицензия не для коммерческих игр |
| Unity Asset Store версия InspectorJ | EULA привязана к Unity; тот же пак есть на Sonniss |
| Little Robot Sound Factory UI | 68 из 105 звуков сделаны ртом |
| GameAudio UI SFX, SoupTonic, Komiku | мультяшный стиль |
| JDSherbert Pixel UI, Eric Skiff «Resistor Anthems» | аркадный и экшен-чиптюн |
| 3rdEchoSounds, Xanderwood «20 fanfare jingles», IndieSFX S035C | нет превью или оценок, либо лицензию не проверить |
| ThiSound, Takenobu Yamana | лицензии нет, или она в одну фразу при треках на стримингах |
| InspectorJ «Ambience, Machine Factory» | собран из звуков принтера с ревербом, настоящего завода нет |
| PimFeijen «LargeWoodenShip» | MP3, записан в деревянном доме, а не на корабле |
| Epic Stock Media Advanced UI ($44), Ovani UI Mega Bundle ($64) | качественно, но для нашей эстетики избыточно; вернуться к ним к релизу |
| **Под вопросом:** BLACKMID Fantasy Orchestral Stingers | вместо лицензии одна рекламная фраза. Не брать, пока автор не пришлёт письменную лицензию |
| **Под вопросом:** Skyscraper Seven «Ancient Egypt» | условия только в PDF внутри покупки ($1) |
| **Под вопросом:** Rune (Chris Logsdon) Fantasy Towns | поверх CC BY запрещены проекты с ИИ-контентом, а игру пишут ИИ-агенты. Не брать без письменного подтверждения |
| Файлы с муэдзином и храмовым пением (Faunethic Jordan, Turkey, India) | узнаваемые реальные обряды. Как фон не берём, только осознанно под систему религий |

---

## 5. План скачивания (ждёт одобрения автора)

Всё ниже скачивает и покупает **автор**. Агенты ничего не качают, не входят в аккаунты и не принимают условий.

### 5.0 Что послушать до одобрения (около 20 минут)

1. Kenney Interface Sounds и UI Audio: плеер на страницах, часть уже в игре.
2. JDSherbert Tabletop: превью на itch, затем бесплатная урезанная версия.
3. Leohpaz Book/Parchment: превью на itch.
4. BigSoundBank: клавиша s2842 и звонок s2844.
5. Ovani Jingles & Stingers: YouTube-превью Vol. 1 и Vol. 2.
6. Ivo Vicic: SoundCloud-превью лесной библиотеки.
7. Felix Blume: Atacama (156414) и Прага (462474) прямо на Freesound.

### 5.1 Куда класть

```
D:/supergae/audio_src/            исходники WAV; вне game/, чтобы Godot их не импортировал; не коммитить
  gdc2017/ gdc2018/ gdc2019/ gdc2020/ gdc2021-23/ freesound/ bigsoundbank/ jdsherbert/ leohpaz/ ovani/
game/assets/audio/
  sfx/          интерфейс (как сейчас): name_N.ogg|wav + LICENSE_*.txt
  world/        точечные звуки карты: шаги, стройка, караван, колокола
  ambience/     подложки биомов amb_*.ogg и слоя цивилизации civ_*.ogg
  stingers/     события и пасхалки
  music/        позже (волна D)
  CREDITS_audio.txt   построчная атрибуция CC BY и обязательные строки
```

### 5.2 Волна A — бесплатно, без аккаунта (≈ 2 ГБ исходников)

1. **Kenney Interface Sounds** — [kenney.nl/assets/interface-sounds](https://kenney.nl/assets/interface-sounds), zip ≈ 0,8 МБ → `sfx/`. Берём недостающие группы: select, switch, toggle, scroll, glass, pluck, bong, question, drop, maximize, minimize, остальные варианты click и error.
2. **Kenney UI Audio** — [kenney.nl/assets/ui-audio](https://kenney.nl/assets/ui-audio), ≈ 1 МБ → `sfx/` (rollover, switch).
3. **Kenney RPG Audio** — [kenney.nl/assets/rpg-audio](https://kenney.nl/assets/rpg-audio), ≈ 1–2 МБ → `sfx/` (handleCoins, metalLatch, book*, cloth).
4. **Kenney Impact Sounds** — [kenney.nl/assets/impact-sounds](https://kenney.nl/assets/impact-sounds), ≈ 2–3 МБ → `world/` (footstep_*, impactMining, impactPlank).
5. **Kenney Music Jingles** — [kenney.nl/assets/music-jingles](https://kenney.nl/assets/music-jingles), ≈ 2–4 МБ → `stingers/` (заглушка: Pizzicato, Hit, Retro).
6. **BigSoundBank, Hermes Precisa 305** — 6 файлов WAV 48/24, ≈ 7 МБ → `audio_src/bigsoundbank/`, нарезка в `sfx/type_N.wav`: [s2842 Key](https://bigsoundbank.com/typewriter-key-s2842.html), [s2843 space](https://bigsoundbank.com/typewriter-space-s2843.html), [s2844 Bell #1](https://bigsoundbank.com/typewriter-bell-s2844.html), [s2845 Bell #2](https://bigsoundbank.com/typewriter-bell-s2845.html), [s2836 #3](https://bigsoundbank.com/typewriter-3-s2836.html) (23 с), [s2837 #4](https://bigsoundbank.com/typewriter-4-s2837.html) (13 с).
7. **Sonniss #GameAudioGDC** — только с официальной страницы [sonniss.com/gameaudiogdc](https://sonniss.com/gameaudiogdc/). Каждый год лежит там ZIP-частями по несколько гигабайт и официальным торрентом. **Проще торрент:** в клиенте снять все галки и отметить только файлы из таблицы. Выйдет ≈ 1,95 ГБ вместо сотен гигабайт. Скачанное кладём в `audio_src/gdcГГГГ/<папка>/`.

   | Бандл | Папка | Файлы | Размер | Куда в игре |
   |---|---|---|---|---|
   | 2017 | Hzandbits - Wind In Trees | `Wind,Ext,Gust,Grasslands,Rustle,Buffeting.wav`; `Wind,Ext,Breeze,Reeds,Rustle,Howl,Whip,Whistle.wav` | 121 + 115 МБ | `ambience/amb_plains`, `amb_swamp` |
   | 2017 | Charlie Atanasyan - Jordan sound library by Faunethic | `Desert canyon quiet, light birds and wind, large reverb_160821_006.wav`; `Market souk medium voices activity and vendor shout _160818_005.wav`; `Village medium in a canyon voices activity and horses, wide reverb_160822_001.wav` | 46 + 25 + 43 МБ | `amb_canyon`, `civ_market`, `civ_village` |
   | 2017 | Charlie Atanasyan - Thailand sound library by Faunethic | `Jungle quiet insects and birds wide _120407_11.wav` | 43 МБ | `amb_jungle` |
   | 2017 | Hzandbits - Money | все 4 файла | 1,9 МБ | `sfx/coins_N` |
   | 2017 | The Soundcatcher - Paperlife | все 4 файла | 2,1 МБ | `sfx/paper_N` |
   | 2018 | Ivo Vicic - Bora wind - European local wind | `03 Wind_grass_ strong_hurricane blast 3.wav`; `14 Wind_dead leaves_whoosh_forest_birds.wav` | 44 + 122 МБ | `amb_storm_sea`, `amb_forest` |
   | 2018 | Discover Oregon - Wind and Storms | `Wind Gusts 2016 Lookout Snow - Biggest Gusts Long.wav`; `Rain 2014 Frogs Rain.wav` | 34 + 11 МБ | `amb_snow` (порывы), `amb_swamp` |
   | 2018 | Discover Oregon - Lava from the Kilauea Volcano in Hawaii | `Lava Lava Windy.WAV`; `Lava Lava Short Windy Crackles.WAV` | 34 + 4 МБ | `amb_volcano`, `stingers/eruption` |
   | 2018 | The Sound Keeper - Small Fire | `sm-fire-firewood-small-03-DPAmono.wav` | 5 МБ | `world/found_fire` |
   | 2019 | Ivo Vicic - Calm sea | `08 Small beach_gentle waves_echo 1.wav`; `59 Beach_wave_near 6.wav` | 165 + 82 МБ | `amb_sea_far`, `amb_sea_near` |
   | 2019 | Ivo Vicic - Natural ambiences vol.1 | `38 Wind in grass_Velebit mountain.WAV`; `05 Crickets_owls_summer night.WAV` | 83 + 82 МБ | `amb_hills`, `amb_night` |
   | 2019 | Ivo Vicic - Winter textures vol.1 | `07 Blizzard_Snow melting_wind_MS stereo.wav`; `58 Wind in pines_mid field_MS stereo.wav`; `91 Wind_winter grass_2_MS stereo.wav` | 97 + 84 + 77 МБ | `amb_snow`, `amb_taiga`, `amb_tundra` |
   | 2019 | Shapeforms - The Mint – Coins and Money | все 4 файла | 0,8 МБ | `sfx/coins_N` |
   | 2020 | Ivo Vicic - Mountain rivers and streams | `74 River_mid field_LOOP 2.WAV`; `63 River_near field_LOOP 11.WAV` | 78 + 40 МБ | `amb_river_far`, `amb_river_near` |
   | 2020 | Ivo Vicic - European mountain forest animals and soundscapes | `185 Soundscape_mountain forest_thunderstorm_close_birds distant_summer.WAV` | 125 МБ | `amb_thunderstorm` |
   | 2020 | Ivo Vicic - Rain in urban and natural environment | `92 Rain_mountain_cabin_porch MS STEREO.WAV` | 35 МБ | `amb_rain` (второй вариант) |
   | 2021-23 | Ivo Vicic - Bellmen - Folk Custom | `22 BELLMEN_Kukuljanski Bell ringers.wav`; `23 BELLMEN_Traditional horns.wav` | 23 + 40 МБ | `world/bellmen`, `stingers/meet_horn` |
   | 2021-23 | Ivo Vicic - Industrial gas boiler room | `23 Industrial Gas Boiler room_room tone_boiler near_PAN C.wav` | 40 МБ | `civ_boiler` |
   | 2021-23 | Ivo Vicic - Northern Mediterranean soundscapes - Adriatic sea | `157 Urban_island_small town_harbour_water lapping against harbour wall and ship hull 02.wav` | 25 МБ | `civ_harbour` |
   | 2021-23 | Mononeshot - Industrial, Ironworks | `INDUSTRIAL, Ironworks - 10 - Machining shop - Ambient noise V2.wav` | 52 МБ | `civ_factory` |
   | 2021-23 | InspectorJ - Essentials 06 Rain | `RAINVege_InsJ_Ambience_Rain_Moderate_02_LOOP.wav`; `RAINConc_InsJ_Ambience_Rain_Moderate_03_LOOP.wav` | 11 + 17 МБ | `amb_rain` |
   | 2021-23 | InspectorJ - Essentials 01 Thunder | все 3 файла | 46 МБ | `world/thunder_N` |
   | 2021-23 | InspectorJ - Essentials 03 Fireworks | `FRWKComr_InsJ_Fireworks-Display_Multi_02-01.wav` | 27 МБ | `stingers/wonder_fireworks` |
   | 2021-23 | InspectorJ - RPG Orchestral Essentials (Music FX) | все 4 файла (пробник платного пака) | 6 МБ | только послушать |
   | 2021-23 | CB Sound Design - Board Games – Gamedesigners Audio Toolkit 01 | все 4 файла | 3,7 МБ | проба `sfx/piece_N` |
   | 2021-23 | 344 Audio - Ultimate Chess SFX | все 4 файла (в том числе `Writing With Pencil 04.wav`) | 6 МБ | `sfx/piece_N`, `sfx/type_quill_N` |
   | 2021-23 | CB Sound Design - Essential Sounds Vol.02 Pencils | все 4 файла | 1,5 МБ | `sfx/type_quill_N` |
   | 2021-23 | CB Sound Design - Essential Sounds Vol.01 Coins | все 4 файла | 1,3 МБ | `sfx/coins_N` |
   | 2021-23 | Justsoundeffects - Clocks and Mechanics | `CLOCKTick_Antique Pocket Watch Ticking 01_JSE_CM_Mono.wav`; `CLOCKMech_Antique Pocket Watch Winding Mechanism 03_JSE_CM_Stereo.wav` | 26 + 8 МБ | `sfx/pause_g4`, `sfx/speed_g4_N` |
   | 2021-23 | Dramatic Cat - Horse Carriage - Draft Horse | `VEHWagn_Wood Cart Roll On Stone Pavement In Courtyard 03_DRCA_HOCA_Kmr81i.wav`; `FEETHors_Draft Horse Walk On On Grass And Dirt MONO_DRCA_HOCA_MKH416.wav` | 5 + 4,5 МБ | `world/cart`, `world/horse` |
   | 2021-23 | Stefano Cremona - Pure Nature Ambiences | `Campfire.wav` | 10 МБ | запасной `civ_campfire` |

   По желанию ещё ≈ 0,2 ГБ: Faunethic Turkey `Market big bazard indoor…` (32 МБ), Thailand `Forest cicadas insects and birds at night…` (52 МБ), Dramatic Cat Olivetti (3 файла, 68 МБ), Ivo Vicic `07 Waterfall near field 7_LOOP.WAV` (39 МБ), Cremona `Crickets.wav` (11 МБ).

### 5.3 Волна B — Freesound (нужен аккаунт; скачивает автор вручную, ≈ 0,7 ГБ)

Перед скачиванием проверить плашку лицензии на каждой странице: у Felix Blume есть и NC-файлы.

8. Felix Blume, «Wind blowing into some cactus spine… Atacama» — [/s/156414](https://freesound.org/people/felix.blume/sounds/156414/), WAV 96/24, 66 МБ, CC0 → `amb_desert`.
9. Felix Blume, «Market in Africa… Diafarabé (Mali)» — [/s/173153](https://freesound.org/people/felix.blume/sounds/173153/), 44 МБ, CC0 → `civ_market` (вариант 2).
10. Felix Blume, «Church Bells at noon on Sunday (Prague)» — [/s/462474](https://freesound.org/people/felix.blume/sounds/462474/), 10 мин 96/24, 335 МБ, CC0 → `civ_bells` и одиночные удары в `world/bell_N`.
11. Felix Blume, «Sheep in a field… Bask Country» — [/s/138424](https://freesound.org/people/felix.blume/sounds/138424/), 33 МБ, CC0 → `civ_pasture`.
12. *(по желанию)* Felix Blume, «Souk ambience far away with slight wind and donkey» — [/s/779853](https://freesound.org/people/felix.blume/sounds/779853/), 99 МБ, CC0 → `civ_city_far`.
13. Spandau, «campfire.wav» — [/s/40699](https://freesound.org/people/Spandau/sounds/40699/), 65 МБ, CC0 → `civ_campfire`.
14. YleArkisto, «Niger, Tuareg market, people, camels, bells, buzz» — [/s/254668](https://freesound.org/people/YleArkisto/sounds/254668/), 35 МБ, **CC BY 4.0** → `world/caravan`.
15. bone666138, «Choir of Voices – Single Chord» — [/s/274121](https://freesound.org/people/bone666138/sounds/274121/), 1 МБ, CC0 → `stingers/religion`.
16. aoristos, «funeral bell – cloche funèbre» — [/s/329324](https://freesound.org/people/aoristos/sounds/329324/), ≈ 5 МБ, CC0 → `stingers/plague`.
17. soundsandrebounds, «A Lover and His Lass» — [/s/769807](https://freesound.org/people/soundsandrebounds/sounds/769807/), 24 МБ, CC0 → `stingers/easter_dance`.
18. benniknop, «Male Yawn…» — [/s/317845](https://freesound.org/people/benniknop/sounds/317845/), ≈ 5 МБ, CC0 → `stingers/easter_yawn`.

### 5.4 Волна C — покупки (фирменный слой, ≈ $50, ≈ 0,3 ГБ)

19. **JDSherbert, Tabletop Games SFX Pack** — [itch](https://jdsherbert.itch.io/tabletop-games-sfx-pack), £4,99, ≈ 24 МБ; сначала бесплатная урезанная версия → `sfx/piece_N`, `sfx/place_N`, `stingers/dice_N`. Файл лицензии из архива положить как `LICENSE_JDSherbert.pdf`.
20. **Leohpaz, Book/Parchment UI SFX** — [itch](https://leohpaz.itch.io/22-bookparchment-ui-sfx), $1,99, 2,8 МБ → `sfx/book_*`, `sfx/page_N`.
21. **Ovani Sound, Jingles & Stingers Vol. 1 + Vol. 2** — [Vol. 1](https://ovanisound.com/products/jingles-sound-fx-pack), [Vol. 2](https://ovanisound.com/products/jingles-stingers-sound-fx-pack-vol-2), по $20, 272 WAV, ≈ 0,2–0,3 ГБ (оценка) → `stingers/`.

### 5.5 Волна D — музыка (только после решения по вопросу 1 из §7)

22. VSCO 2 Community Edition — [versilian-studios.com/vsco-community](https://versilian-studios.com/vsco-community/): облегчённый набор на десятки МБ или полный ≈ 3 ГБ. VCSL — [github.com/sgossner/VCSL](https://github.com/sgossner/VCSL). Это сырьё для лейтмотива, в `audio_src/` (в игру попадает только рендер).
23. Musopen с archive.org — выборочно 8–12 частей в FLAC, ≈ 0,3–0,5 ГБ (весь DVD весит 7,5 ГБ, его не качать) → `music/g4_*.ogg`.
24. Ovani, музыкальные паки — по одному на группу эпох, ≈ $50 и около 1 ГБ WAV каждый (оценка). Лучше дождаться их Humble Bundle.

### 5.6 Итог по объёму

| Волна | Исходники | Деньги | Аккаунт |
|---|---|---|---|
| A: Kenney, BigSoundBank, GDC | ≈ 2 ГБ (+0,2 ГБ по желанию) | 0 | не нужен |
| B: Freesound | ≈ 0,6–0,7 ГБ | 0 | Freesound (автор) |
| C: покупки | ≈ 0,3 ГБ | ≈ $50 | оплата (автор) |
| **В сборке игры после обработки** | **≈ 50–70 МБ OGG/WAV** | | |

### 5.7 Строки для экрана «Авторы» и файлы лицензий

Раздел «ЗВУКИ» в `assets/front/credits.json`. Обязательные строки — первые две. Остальные мы добавляем из вежливости и ради прозрачности.

```
Sounds by JDSherbert – https://jdsherbert.itch.io
"Niger, Tuareg market, people, camels, bells, buzz" — YleArkisto / Yle (Finnish Broadcasting Company), freesound.org/s/254668, CC BY 4.0 (фрагмент, обработка)
Kenney — Interface Sounds, UI Audio, RPG Audio, Impact Sounds, Music Jingles (CC0) · www.kenney.nl
Leohpaz — Book/Parchment UI SFX · leohpaz.itch.io
Sound effects by Ovani Sound · ovanisound.com
Sonniss #GameAudioGDC: Ivo Vicic, Faunethic (Charlie Atanasyan), Hzandbits, Discover Oregon, InspectorJ, Mononeshot, CB Sound Design, 344 Audio, Justsoundeffects, Dramatic Cat, Shapeforms Audio, The Soundcatcher, The Sound Keeper, Stefano Cremona
Joseph SARDIN — BigSoundBank.com (CC0)
Freesound (CC0): felix.blume, Spandau, bone666138, aoristos, soundsandrebounds, benniknop
```

Файлы рядом со звуками:
- `LICENSE_Kenney.txt` — уже есть;
- `LICENSE_JDSherbert.pdf`, `LICENSE_Leohpaz.txt`, `LICENSE_Ovani.txt` — текст условий со страницы;
- `LICENSE_SonnissGDC.txt` — текст [GDC Bundle License](https://sonniss.com/gdc-bundle-license/);
- `LICENSE_CC0_Freesound_BigSoundBank.txt` — список файлов с ссылками;
- `CREDITS_audio.txt` — строки CC BY.

Экран «Авторы» сейчас читает только `assets/audio/sfx/LICENSE_*.txt` (MAIN_MENU §3.5). Надо читать `assets/audio/**/LICENSE_*` и `CREDITS_audio.txt`.

**Гигиена.** Платные звуки и звуки GDC нельзя раздавать как файлы. Внутри `.pck` экспорта всё в порядке, но их нельзя класть в публичный git и в открытую папку модов. Ovani прямо требует, чтобы файлы были вшиты в игру. Если репозиторий станет публичным, `game/assets/audio/` с этими звуками выносим в приватное хранилище. Лицензии Ovani и магазина Sonniss выдаются одному пользователю: покупает автор.

---

## 6. Как встроить в Godot

### 6.1 Шины

Шины создаются кодом, как сейчас (`Sfx.EnsureBus`). Нужно добавить параметр «куда отправлять», по умолчанию `Master`.

```
Master        AudioEffectHardLimiter, потолок −1 dB
├── Music     ползунок «Музыка»
├── Ambience  ползунок «Атмосфера» (новый); AudioEffectLowPassFilter: срез по зуму и паузе
└── SFX       ползунок «Эффекты»; запас −4 dB, как сейчас
    ├── UI        интерфейс (Sfx)
    ├── World     точечные звуки карты
    └── Stingers  события
```

- **Приглушение** делаем твином громкости шин Music и Ambience в момент стингера: −6 и −5 дБ за 0,1 с, возврат за 1,5 с. Твин проще и предсказуемее sidechain-компрессора и работает на паузе. Запасной вариант — `AudioEffectCompressor.Sidechain = "Stingers"` на Music и Ambience.
- **Пауза:** срез low-pass у Ambience 20 кГц → 1,2 кГц и −6 дБ за 0,3 с. Все звуковые автозагрузки работают с `ProcessMode = Always`.
- **Зум:** срез low-pass у Ambience 20 кГц на ближнем плане → 8–10 кГц на дальнем.
- **Настройки:** `[audio] master=80 music=70 ambience=70 sfx=80 background=false`.

### 6.2 Форматы и импорт

| Что | Формат исходника в игре | Импорт в Godot |
|---|---|---|
| Kenney (приходит в OGG) | оставляем OGG | Loop выключен |
| Короткий UI и точечные звуки из WAV-источников | WAV 16 бит, 44,1/48 кГц, **моно** | Compress Mode: QOA (выставить явно); Force Mono включён; Edit Trim включён (срезает хвостовую тишину); Normalize выключен (нормализуем заранее); Loop Mode: Disabled |
| Подложки биомов и цивилизации | OGG Vorbis q5–6 (≈ 160 кбит/с), стерео, 60–120 с | Loop включён; Loop Offset, если у петли есть вступление |
| Стингеры | OGG Vorbis q6, стерео | Loop выключен |
| Музыка | OGG Vorbis q6, стерео | Loop выключен для треков плейлиста; BPM и Beat Count для слоёв интенсивности |

`Sfx.LoadFiles` сейчас ищет только `.ogg`. Добавить `.wav`: импортированный ресурс грузится тем же `GD.Load<AudioStream>`.

### 6.3 Подготовка файлов

- **Инструменты:** Audacity или REAPER для нарезки, ffmpeg для пакетной конвертации и нормализации. Пример:
  `ffmpeg -i in.wav -af loudnorm=I=-22:TP=-1:LRA=7 -ar 48000 -c:a libvorbis -q:a 5 amb_forest.ogg`
- **Петли подложек:** взять спокойный кусок 90 с без речи, моторов и резких событий. Последние 3 с наложить кроссфейдом на начало, резать по нулю. Ещё лучше играть подложку двумя плеерами со взаимным кроссфейдом и случайной точкой старта: так повтор не слышен даже за часы игры.
- **Нарезка одиночных звуков** из длинных дублей (машинка, колокола, фишки): 6–10 лучших ударов, у каждого хвост 50–150 мс с фейдом, затем `name_1…N`.
- **Доиндустриальные эпохи:** вырезать моторы, самолёты, мегафоны и разборчивую речь.

### 6.4 Что поменять в коде (задачи для агентов)

| Файл | Изменение |
|---|---|
| `Core/Audio/Sfx.cs` | загрузка `.wav`; случайный тон ±4 % по умолчанию; смещение громкости на ключ (наведение −8 дБ); шина по ключу (UI или World); таблица «кож» по группе эпох `SetEraGroup(int)`. Для вариантов можно взять встроенный `AudioStreamRandomizer`: он даёт случайный тон и громкость и не повторяет вариант подряд |
| `Core/Audio/Ambience.cs` (новая автозагрузка) | 1–3 активные подложки по долям биомов в кадре с кроссфейдом 2 с; слой цивилизации по эпохе и близости городов; low-pass по зуму и паузе; точечные звуки через `AudioStreamPlayer2D` с `MaxPolyphony` и лимитом частоты |
| `Core/Audio/Stingers.cs` (новая) | очередь с приоритетами (п. 6 §1), не чаще одного раза в 1,5–2 с, приглушение шин, 0,4 с тишины перед сменой эпохи |
| `Core/Audio/Music.cs` | плейлисты по группам эпох, паузы между треками, слои интенсивности (§6.5) |
| `Core/Settings.cs`, экран настроек | ползунок «Атмосфера», шины UI, World, Stingers и Ambience |
| экран «Авторы» | читать `assets/audio/**/LICENSE_*` и `CREDITS_audio.txt` |

### 6.5 Простая адаптивная музыка по эпохам

1. **Состояния:** Меню → Партия (группа эпох, напряжение) → Смена эпохи → Пауза.
2. **Плейлист группы:** 4–8 треков в случайном порядке без повторов. Вход 3 с, уход 4 с, между треками 30–90 с одной атмосферы. Первый трек начинается через 20–40 с после старта партии. В Godot 4.3+ есть `AudioStreamPlaylist` с фейдами. Паузы между треками проще держать таймером в `Music.cs`.
3. **Напряжение:** три версии интенсивности трека Ovani играют синхронно через `AudioStreamSynchronized` (Godot 4.3+), а громкость слоёв плавно меняется за 2–4 с. В мирном MVP напряжение растёт от голода, бедствия, недовольства попов и отставания в гонке эпох, во втором этапе от войны. Если `AudioStreamSynchronized` не подойдёт, те же три `AudioStreamPlayer` с общим стартом.
4. **Смена эпохи:** музыка уходит за 1,5 с → 0,4 с тишины → лейтмотив в инструменте новой группы → 10–20 с одной атмосферы → плейлист новой группы.
5. **Пауза:** музыка продолжает играть на −4 дБ, как в HOI4. Атмосфера глушится фильтром.
6. **Меню:** лейтмотив целиком, когда он появится. До этого тишина (MAIN_MENU §1.5).
7. **Если GDD оставит «музыку общей»,** остаётся один плейлист на всю игру, а пункт 4 работает как есть: эпоху слышно только в фанфаре.

---

## 7. Вопросы автору

1. **Музыка по эпохам.** GDD §9.2 говорит «музыка общая для всех эпох». Меняем на «общий лейтмотив + плейлисты по 5 группам эпох» или оставляем как есть?
2. **Покупки фирменного слоя** примерно на $50: JDSherbert Tabletop, Leohpaz Book/Parchment, Ovani Jingles & Stingers Vol. 1–2. Берём?
3. **Первобытная эпоха без музыки,** только мир, костёр и барабаны. Да или нет?
4. **Лейтмотив:** рендерим сами на VSCO 2 CE или к релизу заказываем композитора?
5. **Репозиторий** будет публичным? Если да, звуки GDC и платные уходят в приватное хранилище.
6. **Отдельный ползунок «Атмосфера»** в настройках (сейчас их три: общая громкость, музыка, эффекты)?
