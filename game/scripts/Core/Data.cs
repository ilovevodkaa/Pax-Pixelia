using PaxPixelia.Core.Flags;

namespace PaxPixelia.Core;

/// <summary>Static game tables (pure C#). Colours are 0..255 RGB triples.</summary>
public static class Data
{
    // ---- biomes (index 0 = water) ----
    public static readonly string[] BiomeName = { "Вода", "Ледник", "Горные пики", "Горы", "Тундра", "Тайга", "Холодная степь", "Болото", "Лес", "Луга", "Равнина", "Степь", "Джунгли", "Саванна", "Пустыня" };
    public static readonly byte[][] BiomeColor = {
        new byte[]{0,0,0}, new byte[]{214,221,228}, new byte[]{232,236,240}, new byte[]{122,116,108}, new byte[]{128,134,112}, new byte[]{58,84,66}, new byte[]{142,142,102},
        new byte[]{74,88,64}, new byte[]{64,102,60}, new byte[]{110,144,74}, new byte[]{144,156,88}, new byte[]{170,160,100}, new byte[]{48,98,56}, new byte[]{172,154,86}, new byte[]{200,180,128} };
    public static readonly float[] BiomeFert = { 0, 0, 0, .05f, .15f, .35f, .4f, .4f, .6f, .9f, .85f, .55f, .5f, .5f, .1f };
    public static string Climate(int b) => b == 1 || b == 2 || b == 4 ? "полярный" : b == 5 || b == 6 ? "холодный" : b >= 12 ? "тропический" : b == 3 ? "горный" : "умеренный";

    // ---- name generator syllables ----
    public static readonly string[] Syl = { "ар","да","мир","ра","кес","ол","ва","тор","ин","ска","лу","бра","вен","ти","го","ря","зан","ель","мо","ши","ка","лин","дор","ас","эн","ул","ор","не","ви","са","ром","ли","тас","хе","бар","ну","гел","сим","та","ур","ис","ма","кор","те","вил","мер","сан","до","рик","ла" };
    public static readonly string[] Suf = { "", "", "", "ия", "ск", "ов", "ин", "ара", "ет", "он", "ея" };

    // ---- nations (ancient era): the default roster. index 0 = the player's slot ----
    // A game's own roster (the player's design at [0]) lives in GameState.Nations / Game.I.Nations — read that one.
    public record Nation(string Name, string Gov, byte R, byte G, byte B, string CultureAdj, int Religion, FlagSpec Flag = default, byte Culture = 0);
    public static readonly Nation[] Nations = {
        new("Ардания","Вождество",190,72,60,"арданская",0), new("Кесарат Мирры","Кесарат",72,112,182,"мирранская",1),
        new("Торн","Племенной союз",112,152,58,"торнская",2), new("Ксилия","Царство",182,130,72,"ксильская",2),
        new("Лура","Вольные города",70,164,154,"лурская",0), new("Ун","Жреческое государство",140,92,172,"унская",3),
        new("Вения","Держава",200,142,46,"венская",2), new("Скаллия","Вождество",92,140,96,"скальская",2),
        new("Ольмерия","Царство",180,96,124,"ольмерская",1), new("Бразан","Каганат",106,112,166,"бразанская",2),
        new("Эльдора","Вождество",158,164,74,"эльдорская",2), new("Гошар","Племенной союз",164,92,58,"гошарская",3),
        new("Тасмир","Город-государство",58,140,182,"тасмирская",0), new("Роменна","Царство",186,156,108,"роменская",2),
        new("Нирея","Вождество",108,178,124,"нирейская",2), new("Барахия","Жреческое государство",142,58,92,"барахская",3) };

    public record Religion(string Name, byte R, byte G, byte B);
    public static readonly Religion[] Religions = { new("Культ Солнца",214,176,84), new("Путь Мирры",152,114,208), new("Древние духи",112,162,112), new("Огненный завет",216,112,64) };

    public record PopClass(string Name, byte R, byte G, byte B);
    /// <summary>Ancient-era classes (they change per era, see GDD). No slaves at the start: a tribe has none.</summary>
    public static readonly PopClass[] AncientClasses = { new("Общинники",0x7f,0xa3,0x5a), new("Жрецы",0xd0,0xad,0x5c), new("Знать",0xb8,0x5c,0x52) };

    public enum Bld : byte { Farm, Lumber, Quarry, Fishery, Pasture, Shrine, Market, Granary }
    public static readonly string[] BldName = { "Ферма", "Лесопилка", "Каменоломня", "Рыбацкая пристань", "Пастбище", "Святилище", "Рынок", "Амбар" };

    public static readonly string[] Ores = { "Медь", "Олово", "Железо", "Золото", "Соль" };
}
