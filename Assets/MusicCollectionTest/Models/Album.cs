namespace MusicCollectionTest.Models
{
    public sealed class Album
    {
        public int Id { get; }
        public string Title { get; }
        public string Artist { get; }
        public int Year { get; }

        public Album(int id, string title, string artist, int year)
        {
            Id = id;
            Title = title ?? string.Empty;
            Artist = artist ?? string.Empty;
            Year = year;
        }
    }
}
