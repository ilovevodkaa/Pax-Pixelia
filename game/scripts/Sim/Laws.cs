using System;

namespace PaxPixelia.Sim;

/// <summary>One variant of a law: from which era it can be passed and where it leans on the political compass
/// (X: left −, right +; Y: liberty −, authority +; −2…2).</summary>
public sealed record LawDef(string Id, string Name, int Era, int X, int Y, string Note = null);

/// <summary>A group of laws (one variant is in force at a time), in a category of the «Законы» tab.</summary>
public sealed record LawGroup(string Id, string Category, string Name, string Hint, LawDef[] Variants);

/// <summary>
/// The laws of a state (the «Правительство» screen, tab «Законы»), in the manner of Victoria 3 but through all eleven
/// eras, from the elders' word to the age of machines. For now only the outline: every group with its variants, the era
/// each opens in and its lean on the compass. A nation lives by the oldest variant of each group; passing laws, their
/// effects and the interest groups behind them come later. Ids never move: new groups and variants are appended.
/// </summary>
public static class Laws
{
    static LawDef L(string id, string name, int era, int x, int y, string note = null) => new(id, name, era, x, y, note);

    public static readonly string[] Categories = { "Власть", "Порядок и сила", "Вера и общество", "Права людей", "Хозяйство" };

    public static readonly LawGroup[] Groups =
    {
        // ---------------------------------------------------------------- Власть
        new("head", "Власть", "Глава государства", "Кто стоит во главе: от старейшины рода до совета машин",
        [
            L("elder", "Старейшина рода", 0, -1, 0), L("chief", "Вождь", 0, 0, 1), L("priest_king", "Царь-жрец", 1, 0, 2),
            L("king", "Царь", 1, 1, 2), L("emperor", "Император", 2, 1, 2), L("doge", "Дож", 3, 1, 0, "выборный правитель торговой республики"),
            L("crowned_parliament", "Монарх при парламенте", 5, 1, -1), L("president", "Президент", 5, 0, -1),
            L("chancellor", "Канцлер", 6, 1, 0), L("general_secretary", "Генеральный секретарь", 7, -2, 2),
            L("ai_council", "Совет разумных машин", 10, 0, 1),
        ]),
        new("succession", "Власть", "Как приходят к власти", "Как держава выбирает своего главу",
        [
            L("seniority", "Старшинство", 0, -1, 0), L("warriors_choice", "Выбор воинов", 0, 0, 1), L("bloodline", "Наследование по крови", 1, 1, 2),
            L("lot", "Жребий", 2, -1, -2, "как в древних Афинах"), L("nobles_choice", "Выбор знати", 2, 1, 1), L("elective_monarchy", "Выборная монархия", 3, 1, 0),
            L("census_vote", "Выборы по цензу", 5, 1, -1), L("universal_vote", "Всеобщие выборы", 6, 0, -2), L("party_decides", "Решает партия", 7, -1, 2),
            L("exam", "Отбор по экзамену", 8, 0, 0), L("liquid_democracy", "Цифровое прямое правление", 9, -1, -2),
        ]),
        new("pillar", "Власть", "Опора власти", "На кого опирается правитель",
        [
            L("clan", "Род", 0, -1, 0), L("retinue", "Дружина", 0, 0, 1), L("priesthood", "Жречество", 1, 0, 2), L("nobility", "Знать", 1, 1, 1),
            L("merchants", "Купечество", 2, 2, -1), L("bureaucracy", "Чиновники", 3, 0, 1), L("people", "Народ", 5, -1, -2),
            L("army", "Армия", 6, 0, 2), L("corporations", "Корпорации", 8, 2, 0), L("technocrats", "Технократы", 9, 0, 1),
        ]),
        new("council", "Власть", "Совет при правителе", "Кто говорит рядом с троном",
        [
            L("none", "Никого", 0, 0, 2), L("elders_council", "Совет старейшин", 0, -1, 0), L("folkmoot", "Вече", 1, -1, -2),
            L("senate", "Сенат", 2, 1, 0), L("duma", "Дума знати", 3, 1, 1), L("estates", "Собрание сословий", 4, 0, 0),
            L("parliament", "Парламент", 5, 0, -1), L("bicameral", "Двухпалатный парламент", 6, 1, -1), L("soviets", "Советы", 7, -2, 0),
            L("online_assembly", "Сетевое собрание", 9, -1, -2),
        ]),
        new("administration", "Власть", "Управление", "Кто правит провинциями от имени державы",
        [
            L("chiefs_word", "Слово вождя", 0, 0, 1), L("temple_scribes", "Храмовые писцы", 1, 0, 1), L("satraps", "Наместники", 2, 0, 2),
            L("fiefs", "Уделы знати", 3, 1, 1), L("hereditary_offices", "Наследственные чины", 3, 1, 1), L("appointed", "Назначенные чиновники", 4, 0, 1),
            L("mandarins", "Чиновники по экзамену", 4, 0, 0), L("elected_officials", "Выборные чиновники", 6, 0, -1),
            L("federation", "Федерация земель", 6, 0, -1), L("digital_state", "Цифровое государство", 8, 0, 1),
        ]),

        // ---------------------------------------------------------------- Порядок и сила
        new("army", "Порядок и сила", "Войско", "Кто берёт в руки оружие",
        [
            L("clan_levy", "Ополчение рода", 0, -1, 0), L("retinue_army", "Дружина вождя", 0, 0, 1), L("chariots", "Колесничная знать", 1, 1, 1),
            L("citizen_hoplites", "Граждане-воины", 2, 0, -1), L("legions", "Легионы", 2, 0, 1), L("feudal_levy", "Феодальное ополчение", 3, 1, 1),
            L("mercenaries", "Наёмники", 4, 2, 0), L("standing_army", "Постоянная армия", 4, 0, 2), L("conscription", "Всеобщая повинность", 6, -1, 2),
            L("professionals", "Профессиональная армия", 8, 1, 0), L("militia", "Народная милиция", 6, -1, -1), L("drones", "Армия машин", 9, 0, 1),
        ]),
        new("order", "Порядок и сила", "Внутренний порядок", "Кто стережёт покой державы",
        [
            L("blood_feud", "Каждый за себя", 0, 0, -2), L("elders_watch", "Дозор старейшин", 0, -1, 0), L("city_watch", "Городская стража", 2, 0, 0),
            L("secret_office", "Тайная канцелярия", 4, 0, 2), L("gendarmes", "Жандармерия", 5, 1, 1), L("secret_police", "Тайная полиция", 7, 0, 2),
            L("guaranteed_liberties", "Гарантии свобод", 6, 0, -2), L("total_watch", "Всеобщее наблюдение", 9, 0, 2),
        ]),
        new("justice", "Порядок и сила", "Суд", "Как решают споры и карают вину",
        [
            L("feud", "Кровная месть", 0, 0, 0), L("elders_court", "Суд старейшин", 0, -1, 0), L("ordeal", "Испытание огнём и водой", 1, 0, 1),
            L("code", "Свод законов на камне", 1, 0, 1, "как у Хаммурапи"), L("roman_law", "Право и судьи", 2, 1, 0), L("church_court", "Церковный суд", 3, 0, 2),
            L("jury", "Суд присяжных", 5, 0, -1), L("independent_courts", "Независимый суд", 6, 0, -2), L("tribunals", "Особые тройки", 7, -1, 2),
            L("algorithmic_court", "Суд алгоритма", 9, 0, 1),
        ]),
        new("war_conduct", "Порядок и сила", "Правила войны", "Чего войско не смеет делать — или смеет",
        [
            L("no_rules", "Горе побеждённым", 0, 0, 2), L("warrior_honor", "Честь воина", 1, 0, 0), L("ransom", "Выкуп за пленных", 3, 1, 0),
            L("truce_of_god", "Божий мир", 3, 0, 1), L("articles_of_war", "Воинский устав", 4, 0, 1), L("conventions", "Конвенции о войне", 6, 0, -1),
            L("war_crimes_tribunal", "Суд за военные преступления", 7, 0, -2),
        ]),

        // ---------------------------------------------------------------- Вера и общество
        new("faith_state", "Вера и общество", "Вера и власть", "Кто ближе к небу — трон или храм",
        [
            L("shamans", "Шаманы при вожде", 0, 0, 1), L("god_king", "Царь — сын богов", 1, 1, 2), L("state_temple", "Государственный храм", 1, 0, 2),
            L("tolerance", "Терпимость к чужим богам", 2, 0, -1), L("state_church", "Государственная церковь", 3, 1, 2), L("theocracy", "Власть духовенства", 3, 0, 2),
            L("freedom_of_conscience", "Свобода совести", 5, 0, -2), L("secular_state", "Светское государство", 6, -1, -1), L("state_atheism", "Государственное безбожие", 7, -2, 2),
        ]),
        new("estates", "Вера и общество", "Сословия", "Как люди делятся на верхних и нижних",
        [
            L("kinfolk", "Все — родичи", 0, -2, 0), L("slavery", "Рабство", 1, 2, 2), L("castes", "Касты", 1, 1, 2), L("serfdom", "Крепостное право", 3, 2, 2),
            L("estates_realm", "Сословия", 3, 1, 1), L("guild_liberties", "Цеховые вольности", 4, 0, -1), L("equality_before_law", "Равенство перед законом", 5, 0, -2),
            L("classless", "Бесклассовое общество", 7, -2, 1),
        ]),
        new("citizenship", "Вера и общество", "Подданство", "Кого держава считает своим",
        [
            L("own_clan", "Только свой род", 0, 0, 1), L("tribal_union", "Союз племён", 0, -1, 0), L("ruling_people", "Народ-господин", 1, 1, 2),
            L("tributaries", "Покорённые — данники", 1, 1, 2), L("citizenship_by_service", "Гражданство за службу", 2, 0, 0), L("subjects_of_crown", "Подданные короны", 3, 1, 1),
            L("equal_peoples", "Равенство народов", 6, -1, -2), L("world_citizenship", "Гражданство мира", 9, -1, -2),
        ]),
        new("education", "Вера и общество", "Учение", "Кто и чему учит детей",
        [
            L("tales", "Сказы старших", 0, 0, 0), L("temple_schools", "Храмовые школы", 1, 0, 1), L("academies", "Академии мудрецов", 2, 1, -1),
            L("monastery_schools", "Монастырские школы", 3, 0, 1), L("universities", "Университеты", 4, 0, -1), L("private_schools", "Частные школы", 5, 2, 0),
            L("public_schools", "Всеобщая школа", 6, -1, 0), L("free_university", "Бесплатное высшее учение", 7, -2, 0), L("neural_learning", "Знания по сети", 9, 0, -1),
        ]),
        new("health", "Вера и общество", "Лечение", "Кто лечит больных",
        [
            L("healers", "Знахари", 0, 0, 0), L("temple_healers", "Храмовые лекари", 1, 0, 1), L("monastery_hospitals", "Больницы при монастырях", 3, -1, 1),
            L("charity_hospitals", "Благотворительные лечебницы", 5, 1, -1), L("insurance", "Страховая медицина", 6, 2, 0), L("public_health", "Государственная медицина", 7, -2, 0),
            L("nanomedicine", "Нанолечение для всех", 10, -1, -1),
        ]),
        new("migration", "Вера и общество", "Чужаки", "Кого пускают жить в державе",
        [
            L("closed_clan", "Род не принимает чужих", 0, 0, 2), L("guest_right", "Право гостя", 0, -1, -1), L("craftsmen_welcome", "Ремесленники желанны", 2, 1, -1),
            L("colonists", "Переселенцы желанны", 4, 0, 0), L("quotas", "Квоты", 6, 0, 1), L("closed_borders", "Закрытые границы", 7, 0, 2), L("open_world", "Открытый мир", 9, 0, -2),
        ]),

        // ---------------------------------------------------------------- Права людей
        new("speech", "Права людей", "Слово и печать", "Что можно говорить вслух",
        [
            L("elders_word", "Слово старейшины — закон", 0, 0, 1), L("exile_of_mockers", "Хулителей изгоняют", 1, 0, 2), L("censorship", "Цензура", 4, 0, 2),
            L("assembly_right", "Свобода собраний", 5, 0, -1), L("free_press", "Свобода печати", 5, 1, -2), L("protected_speech", "Защищённое слово", 6, 0, -2),
            L("propaganda", "Единая правда", 7, -1, 2), L("filtered_net", "Фильтры сети", 8, 0, 2),
        ]),
        new("women", "Права людей", "Положение женщин", "Что позволено половине народа",
        [
            L("clan_custom", "Обычай рода", 0, -1, 0), L("guardianship", "Опека мужа", 1, 1, 1), L("inheritance", "Право наследовать", 2, 0, 0),
            L("work_outside", "Труд вне дома", 6, 0, -1), L("suffrage", "Избирательное право", 6, 0, -2), L("full_equality", "Полное равенство", 7, -1, -2),
        ]),
        new("children", "Права людей", "Дети", "Работают ли дети и учатся ли",
        [
            L("family_work", "Дети трудятся с семьёй", 0, 0, 0), L("apprenticeship", "Ученичество", 3, 1, 0), L("child_labor", "Детский труд на фабриках", 5, 2, 0),
            L("restricted_labor", "Ограничение детского труда", 6, 0, -1), L("compulsory_school", "Обязательная школа", 6, -1, 1),
        ]),
        new("labor", "Права людей", "Труд", "Сколько и на каких условиях работают люди",
        [
            L("clan_labor", "Труд на род", 0, -2, 0), L("corvee", "Повинность на царя", 1, 0, 2), L("guilds", "Цеха", 3, 0, 1),
            L("free_hire", "Вольный наём без прав", 5, 2, 0), L("factory_inspection", "Фабричные инспекции", 6, -1, 0), L("unions", "Профсоюзы", 6, -1, -1),
            L("eight_hours", "Восьмичасовой день", 7, -1, -1), L("labor_camps", "Трудовые лагеря", 7, -1, 2), L("basic_income", "Безусловный доход", 9, -2, -1),
        ]),
        new("welfare", "Права людей", "Призрение бедных", "Кто кормит тех, кто не может сам",
        [
            L("clan_feeds", "Род кормит своих", 0, -1, 0), L("temple_alms", "Храмовая милостыня", 1, 0, 1), L("grain_dole", "Хлебные раздачи", 2, -1, 1),
            L("workhouses", "Работные дома", 5, 1, 1), L("benefits", "Пособия", 6, -1, 0), L("pensions", "Пенсии по старости", 6, -1, 0), L("welfare_state", "Всеобщее благосостояние", 7, -2, 0),
        ]),

        // ---------------------------------------------------------------- Хозяйство
        new("economy", "Хозяйство", "Устройство хозяйства", "Кто решает, что делать и кому продавать",
        [
            L("communal", "Общинное", 0, -2, 0), L("palace_economy", "Дворцовое хозяйство", 1, 0, 2), L("temple_economy", "Храмовое хозяйство", 1, 0, 2),
            L("traditional", "Традиционное", 2, 0, 1), L("mercantilism", "Меркантилизм", 4, 1, 1), L("interventionism", "Вмешательство казны", 6, 0, 1),
            L("laissez_faire", "Свободный рынок", 5, 2, -1), L("cooperatives", "Кооперативы", 6, -2, -1), L("command_economy", "Плановое хозяйство", 7, -2, 2),
            L("platform_economy", "Хозяйство платформ", 9, 2, 0),
        ]),
        new("land", "Хозяйство", "Земля", "Кому принадлежит земля и кто её пашет",
        [
            L("clan_land", "Общая земля рода", 0, -2, 0), L("royal_land", "Царская земля", 1, 0, 2), L("estates_land", "Поместья знати", 2, 1, 1),
            L("serf_plots", "Наделы крепостных", 3, 1, 2), L("tenants", "Арендаторы", 4, 1, 0), L("homesteads", "Фермерские хозяйства", 6, 1, -1),
            L("collectivization", "Коллективизация", 7, -2, 2), L("agroholdings", "Агрохолдинги", 8, 2, 0), L("vertical_farms", "Городские фермы", 9, 0, 0),
        ]),
        new("trade", "Хозяйство", "Торговля", "Как держава торгует с соседями",
        [
            L("gift_exchange", "Обмен дарами", 0, -1, 0), L("caravan_tolls", "Караванные пошлины", 1, 0, 1), L("crown_monopoly", "Монополия казны", 2, 0, 2),
            L("staple_rights", "Право склада", 3, 1, 1), L("protectionism", "Покровительство своим", 5, 0, 1), L("free_trade", "Свободная торговля", 6, 2, -1),
            L("isolation", "Закрытая держава", 4, 0, 2), L("trade_blocs", "Торговые союзы", 8, 1, 0),
        ]),
        new("taxes", "Хозяйство", "Подати", "С чего и сколько берёт казна",
        [
            L("tribute", "Дань вождю", 0, 0, 1), L("tithe", "Десятина", 1, 0, 1), L("poll_tax", "Подушная подать", 2, 0, 1), L("land_tax", "Поземельный налог", 3, 1, 0),
            L("tax_farming", "Откуп", 3, 2, 0), L("excise", "Акцизы", 4, 0, 0), L("income_tax", "Подоходный налог", 6, -1, 0), L("progressive_tax", "Прогрессивный налог", 6, -2, 0),
            L("flat_tax", "Плоская шкала", 8, 2, -1),
        ]),
        new("money", "Хозяйство", "Деньги", "Чем платят на рынке",
        [
            L("barter", "Обмен", 0, 0, 0), L("bullion", "Слитки по весу", 1, 0, 0), L("coinage", "Монета правителя", 2, 0, 1), L("bills", "Векселя", 4, 2, -1),
            L("paper_money", "Бумажные деньги", 5, 0, 1), L("gold_standard", "Золотой стандарт", 6, 2, 0), L("fiat", "Деньги казны без золота", 7, 0, 1),
            L("crypto", "Цифровая монета", 9, 1, -2),
        ]),
        new("colonies", "Хозяйство", "Дальние земли", "Что делать с землями за морем",
        [
            L("none_far", "Дальних земель нет", 0, 0, 0), L("trading_posts", "Торговые фактории", 2, 1, 0), L("settler_colonies", "Переселенческие колонии", 4, 0, 0),
            L("exploitation", "Выжимать колонии", 5, 2, 2), L("protectorates", "Протектораты", 6, 1, 1), L("decolonization", "Отпустить колонии", 7, -1, -2),
            L("space_colonies", "Колонии в космосе", 9, 0, 0),
        ]),
    };

    public static int VariantCount
    {
        get { int k = 0; foreach (var g in Groups) k += g.Variants.Length; return k; }
    }

    /// <summary>The variant in force for a nation: the oldest of each group, for now (passing laws comes later).</summary>
    public static int InForce(NationState nat, int group) => 0;

    /// <summary>The compass in words: «левее, к власти».</summary>
    public static string Lean(int x, int y)
    {
        string h = x < 0 ? (x <= -2 ? "сильно левее" : "левее") : x > 0 ? (x >= 2 ? "сильно правее" : "правее") : null;
        string v = y > 0 ? (y >= 2 ? "к сильной власти" : "к власти") : y < 0 ? (y <= -2 ? "к большой свободе" : "к свободе") : null;
        return h == null && v == null ? "в центре" : h == null ? v : v == null ? h : h + ", " + v;
    }
}
