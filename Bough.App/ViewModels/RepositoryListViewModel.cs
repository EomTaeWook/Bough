using Bough.App.Localization;
using Dignus.DependencyInjection.Attributes;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Bough.App.ViewModels.Models;

namespace Bough.App.ViewModels
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class RepositoryListViewModel
    {
        private readonly RepositoryListStore _store;
        private readonly StringHelper _stringHelper;
        private readonly StringComparer _pathComparer;
        private string _lastActivePath;

        public RepositoryListViewModel(RepositoryListStore store, StringHelper stringHelper)
        {
            _store = store;
            _stringHelper = stringHelper;
            if (OperatingSystem.IsWindows() == true)
            {
                _pathComparer = StringComparer.OrdinalIgnoreCase;
            }
            else
            {
                _pathComparer = StringComparer.Ordinal;
            }
            RepositoryGroups = [];

            RepositoryListState saved = _store.Load();
            _lastActivePath = saved.LastActivePath;
            foreach (string path in saved.Paths ?? [])
            {
                try
                {
                    AddRepository(Path.GetFullPath(path));
                }
                catch (ArgumentException)
                {
                    // Ignore paths that cannot be represented on this platform.
                }
                catch (NotSupportedException)
                {
                    // Ignore paths that cannot be represented on this platform.
                }
            }
        }

        public ObservableCollection<RepositoryGroup> RepositoryGroups { get; }

        public string LastActivePath { get { return _lastActivePath; } }
        public string RepositoriesHeadingText { get { return _stringHelper.GetString("RepositoriesHeading"); } }
        public string AddButtonText { get { return _stringHelper.GetString("AddButton"); } }
        public string AddRepositoryTooltipText { get { return _stringHelper.GetString("AddRepositoryTooltip"); } }
        public string RemoveFromListTooltipText { get { return _stringHelper.GetString("RemoveFromListTooltip"); } }

        public void RememberOpened(string rootPath)
        {
            AddRepository(rootPath);
            SetActive(rootPath);
            _lastActivePath = rootPath;
        }

        public bool Remove(RepositoryItem item)
        {
            if (item == null)
            {
                return false;
            }

            RepositoryGroup group = RepositoryGroups.FirstOrDefault(candidate => candidate.Repositories.Contains(item));
            if (group == null)
            {
                return false;
            }

            group.Repositories.Remove(item);
            if (group.Repositories.Count == 0)
            {
                RepositoryGroups.Remove(group);
            }
            if (_pathComparer.Equals(_lastActivePath, item.RootPath))
            {
                _lastActivePath = null;
            }
            return true;
        }

        public void SetActive(string rootPath)
        {
            foreach (RepositoryItem item in RepositoryGroups.SelectMany(group => group.Repositories))
            {
                item.IsActive = _pathComparer.Equals(item.RootPath, rootPath);
            }
        }

        public void Save()
        {
            _store.Save(new RepositoryListState
            {
                Paths = RepositoryGroups.SelectMany(group => group.Repositories).Select(item => item.RootPath).ToList(),
                LastActivePath = _lastActivePath
            });
        }

        private void AddRepository(string rootPath)
        {
            if (RepositoryGroups.SelectMany(group => group.Repositories)
                .Any(item => _pathComparer.Equals(item.RootPath, rootPath)) == true)
            {
                return;
            }

            string parentPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(rootPath)) ?? rootPath;
            RepositoryGroup group = RepositoryGroups.FirstOrDefault(candidate => _pathComparer.Equals(candidate.ParentPath, parentPath));
            if (group == null)
            {
                group = new RepositoryGroup(parentPath);
                int groupIndex = 0;
                while (groupIndex < RepositoryGroups.Count &&
                    StringComparer.OrdinalIgnoreCase.Compare(RepositoryGroups[groupIndex].Name, group.Name) < 0)
                {
                    groupIndex++;
                }
                RepositoryGroups.Insert(groupIndex, group);
            }

            RepositoryItem itemToAdd = new(rootPath);
            int itemIndex = 0;
            while (itemIndex < group.Repositories.Count &&
                StringComparer.OrdinalIgnoreCase.Compare(group.Repositories[itemIndex].Name, itemToAdd.Name) < 0)
            {
                itemIndex++;
            }
            group.Repositories.Insert(itemIndex, itemToAdd);
        }
    }
}
