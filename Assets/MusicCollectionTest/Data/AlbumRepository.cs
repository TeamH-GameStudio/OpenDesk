using System.Collections.Generic;
using MusicCollectionTest.Models;

namespace MusicCollectionTest.Data
{
    public static class AlbumRepository
    {
        public static IReadOnlyList<Album> GetAll()
        {
            return new List<Album>
            {
                new Album(1,  "Lunar Tide",            "The Cosmic Drift",   2019),
                new Album(2,  "Midnight Static",       "Atlas Echo",         2021),
                new Album(3,  "Velvet Reverie",        "Sera Vesper",        2017),
                new Album(4,  "Sunset Avenue",         "The Mojaves",        2015),
                new Album(5,  "Concrete Jungle",       "Sage Marlow",        2020),
                new Album(6,  "Sapphire Bloom",        "Lila Wren",          2018),
                new Album(7,  "Ironheart Symphony",    "Vermillion Hour",    2014),
                new Album(8,  "Aurora Static",         "Nova Pulse",         2022),
                new Album(9,  "Driftwood Sermons",     "Sister Magnolia",    2016),
                new Album(10, "Quantum Lullaby",       "The Pendulum Theory",2023),
                new Album(11, "Smokehouse Confessions","Ezra Vaughn",        2019),
                new Album(12, "After Hours Boulevard", "Cassia Knox",        2021),
                new Album(13, "Glass Cathedrals",      "Halcyon Wren",       2013),
                new Album(14, "Paper Lions",           "The Indigo Hours",   2016),
                new Album(15, "Bones and Brass",       "Marigold Court",     2020),
                new Album(16, "Cobalt Skies",          "Ulysses North",      2018),
                new Album(17, "The Loneliness Engine", "Phantom Index",      2022),
                new Album(18, "Honeycomb Hotel",       "Junebug Faye",       2024),
                new Album(19, "Salt and Static",       "The Gulf Bell",      2015),
                new Album(20, "Wildwood Anthem",       "Otis and Pearl",     2017),
                new Album(21, "Polar Magnolia",        "Saga Field",         2021),
                new Album(22, "Lighthouse Sessions",   "Theodore Vance",     2019),
                new Album(23, "Brass Hymnal",          "The Iron Plain",     2014),
                new Album(24, "Neon Pastoral",         "Mara Vance",         2023),
                new Album(25, "Wax and Wire",          "Halverson Twins",    2016),
                new Album(26, "Slow Cartography",      "Bridget Hale",       2018),
                new Album(27, "Glacier Letters",       "The Bright Field",   2020),
                new Album(28, "Tarot Television",      "Velveteen Rust",     2022),
                new Album(29, "Underwater Tennis",     "Augustine Cole",     2017),
                new Album(30, "Saint of Mondays",      "The Tin Choir",      2024)
            };
        }
    }
}
