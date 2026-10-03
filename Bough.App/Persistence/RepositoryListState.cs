using System.Collections.Generic;

namespace Bough.App.Persistence
{
    public class RepositoryListState
    {
        public List<string> Paths { get; set; } = [];

        public string LastActivePath { get; set; }
    }
}
