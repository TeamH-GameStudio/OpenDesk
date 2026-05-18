using System.Collections.Generic;
using TestPokedex.Models;

namespace TestPokedex.Data
{
    public static class PokemonRepository
    {
        public static IReadOnlyList<Pokemon> GetAll()
        {
            return new List<Pokemon>
            {
                new Pokemon(1,  "Bulbasaur",   "Grass",    "Poison",   45, 49, 49, 45, "A strange seed was planted on its back at birth. The plant sprouts and grows with this Pokemon."),
                new Pokemon(2,  "Ivysaur",     "Grass",    "Poison",   60, 62, 63, 60, "When the bulb on its back grows large, it appears to lose the ability to stand on its hind legs."),
                new Pokemon(3,  "Venusaur",    "Grass",    "Poison",   80, 82, 83, 80, "The plant blooms when it is absorbing solar energy. It stays on the move to seek sunlight."),
                new Pokemon(4,  "Charmander",  "Fire",     null,       39, 52, 43, 65, "Obviously prefers hot places. When it rains, steam is said to spout from the tip of its tail."),
                new Pokemon(5,  "Charmeleon",  "Fire",     null,       58, 64, 58, 80, "When it swings its burning tail, it elevates the air temperature to unbearably high levels."),
                new Pokemon(6,  "Charizard",   "Fire",     "Flying",   78, 84, 78, 100, "Spits fire that is hot enough to melt boulders. Known to unintentionally cause forest fires."),
                new Pokemon(7,  "Squirtle",    "Water",    null,       44, 48, 65, 43, "After birth, its back swells and hardens into a shell. Sprays a potent foam from its mouth."),
                new Pokemon(8,  "Wartortle",   "Water",    null,       59, 63, 80, 58, "Often hides in water to stalk unwary prey. For swimming fast, it moves its ears to maintain balance."),
                new Pokemon(9,  "Blastoise",   "Water",    null,       79, 83, 100, 78, "A brutal Pokemon with pressurized water jets on its shell. They are used for high-speed tackles."),
                new Pokemon(10, "Caterpie",    "Bug",      null,       45, 30, 35, 45, "Its short feet are tipped with suction pads that enable it to tirelessly climb slopes and walls."),
                new Pokemon(11, "Metapod",     "Bug",      null,       50, 20, 55, 30, "This Pokemon is vulnerable to attack while its shell is soft, exposing its weak and tender body."),
                new Pokemon(12, "Butterfree",  "Bug",      "Flying",   60, 45, 50, 70, "In battle, it flaps its wings at high speed to release highly toxic dust into the air."),
                new Pokemon(13, "Weedle",      "Bug",      "Poison",   40, 35, 30, 50, "Often found in forests, eating leaves. It has a sharp venomous stinger on its head."),
                new Pokemon(14, "Kakuna",      "Bug",      "Poison",   45, 25, 50, 35, "Almost incapable of moving, this Pokemon can only harden its shell to protect itself from predators."),
                new Pokemon(15, "Beedrill",    "Bug",      "Poison",   65, 90, 40, 75, "Flies at high speed and attacks using its large venomous stingers on its forelegs and tail."),
                new Pokemon(16, "Pidgey",      "Normal",   "Flying",   40, 45, 40, 56, "A common sight in forests and woods. It flaps its wings at ground level to kick up blinding sand."),
                new Pokemon(17, "Pidgeotto",   "Normal",   "Flying",   63, 60, 55, 71, "Very protective of its sprawling territorial area, this Pokemon will fiercely peck at any intruder."),
                new Pokemon(18, "Pidgeot",     "Normal",   "Flying",   83, 80, 75, 101, "When hunting, it skims the surface of water at high speed to pick off unwary prey such as Magikarp."),
                new Pokemon(19, "Rattata",     "Normal",   null,       30, 56, 35, 72, "Bites anything when it attacks. Small and very quick, it is a common sight in many places."),
                new Pokemon(20, "Raticate",    "Normal",   null,       55, 81, 60, 97, "It uses its whiskers to maintain its balance. It apparently slows down if they are cut off."),
                new Pokemon(21, "Spearow",     "Normal",   "Flying",   40, 60, 30, 70, "Eats bugs in grassy areas. It has to flap its short wings at high speed to stay airborne."),
                new Pokemon(22, "Fearow",      "Normal",   "Flying",   65, 90, 65, 100, "With its huge and magnificent wings, it can keep aloft without ever having to land for rest."),
                new Pokemon(23, "Ekans",       "Poison",   null,       35, 60, 44, 55, "Moves silently and stealthily. Eats the eggs of birds, such as Pidgey and Spearow, whole."),
                new Pokemon(24, "Arbok",       "Poison",   null,       60, 95, 69, 80, "It is rumored that the ferocious warning markings on its belly differ from area to area."),
                new Pokemon(25, "Pikachu",     "Electric", null,       35, 55, 40, 90, "When several of these Pokemon gather, their electricity could build and cause lightning storms."),
                new Pokemon(26, "Raichu",      "Electric", null,       60, 90, 55, 110, "Its long tail serves as a ground to protect itself from its own high-voltage power."),
                new Pokemon(27, "Sandshrew",   "Ground",   null,       50, 75, 85, 40, "Burrows deep underground in arid locations far from water. It only emerges to hunt for food."),
                new Pokemon(28, "Sandslash",   "Ground",   null,       75, 100, 110, 65, "Curls up into a spiny ball when threatened. It can roll while curled up to attack or escape."),
                new Pokemon(29, "Nidoran F",   "Poison",   null,       55, 47, 52, 41, "Although small, its venomous barbs render this Pokemon dangerous. The female has smaller horns."),
                new Pokemon(30, "Nidorina",    "Poison",   null,       70, 62, 67, 56, "The female's horn develops slowly. Prefers physical attacks such as clawing and biting."),
                new Pokemon(31, "Nidoqueen",   "Poison",   "Ground",   90, 92, 87, 76, "Its hard scales provide strong protection. It uses its hefty bulk to execute powerful moves."),
                new Pokemon(32, "Nidoran M",   "Poison",   null,       46, 57, 40, 50, "Stiffens its ears to sense danger. The larger its horns, the more powerful its secreted venom."),
                new Pokemon(33, "Nidorino",    "Poison",   null,       61, 72, 57, 65, "An aggressive Pokemon that is quick to attack. The horn on its head secretes a powerful venom."),
                new Pokemon(34, "Nidoking",    "Poison",   "Ground",   81, 102, 77, 85, "It uses its powerful tail in battle to smash, constrict, then break the prey's bones."),
                new Pokemon(35, "Clefairy",    "Fairy",    null,       70, 45, 48, 35, "Its magical and cute appeal has many admirers. It is rare and found only in certain areas."),
                new Pokemon(36, "Clefable",    "Fairy",    null,       95, 70, 73, 60, "A timid fairy Pokemon that is rarely seen. It will run and hide the moment it senses people."),
                new Pokemon(37, "Vulpix",      "Fire",     null,       38, 41, 40, 65, "At the time of birth, it has just one tail. The tail splits from its tip as it grows older."),
                new Pokemon(38, "Ninetales",   "Fire",     null,       73, 76, 75, 100, "Very smart and very vengeful. Grabbing one of its many tails could result in a 1000-year curse."),
                new Pokemon(39, "Jigglypuff",  "Normal",   "Fairy",    115, 45, 20, 20, "When its huge eyes light up, it sings a mysteriously soothing melody that lulls its enemies to sleep."),
                new Pokemon(40, "Wigglytuff",  "Normal",   "Fairy",    140, 70, 45, 45, "The body is soft and rubbery. When angered, it will suck in air and inflate itself to an enormous size."),
                new Pokemon(41, "Zubat",       "Poison",   "Flying",   40, 45, 35, 55, "Forms colonies in perpetually dark places. Uses ultrasonic waves to identify and approach targets."),
                new Pokemon(42, "Golbat",      "Poison",   "Flying",   75, 80, 70, 90, "Once it strikes, it will not stop draining energy from the victim even if it gets too heavy to fly."),
                new Pokemon(43, "Oddish",      "Grass",    "Poison",   45, 50, 55, 30, "During the day, it keeps its face buried in the ground. At night, it wanders around sowing its seeds."),
                new Pokemon(44, "Gloom",       "Grass",    "Poison",   60, 65, 70, 40, "The fluid that oozes from its mouth isn't drool. It is a nectar that is used to attract prey."),
                new Pokemon(45, "Vileplume",   "Grass",    "Poison",   75, 80, 85, 50, "The larger its petals, the more toxic pollen it contains. Its big head is heavy and hard to hold up."),
                new Pokemon(46, "Paras",       "Bug",      "Grass",    35, 70, 55, 25, "Burrows to suck tree roots. The mushrooms on its back grow by drawing nutrients from the bug host."),
                new Pokemon(47, "Parasect",    "Bug",      "Grass",    60, 95, 80, 30, "A host-parasite pair in which the parasite mushroom has taken over the host bug. Prefers damp places."),
                new Pokemon(48, "Venonat",     "Bug",      "Poison",   60, 55, 50, 45, "Lives in the shadows of tall trees where it eats insects. It is attracted by light at night."),
                new Pokemon(49, "Venomoth",    "Bug",      "Poison",   70, 65, 60, 90, "The dust-like scales covering its wings are color coded to indicate the kinds of poison it has."),
                new Pokemon(50, "Diglett",     "Ground",   null,       10, 55, 25, 95, "Lives about one yard underground where it feeds on plant roots. It sometimes appears aboveground.")
            };
        }
    }
}
