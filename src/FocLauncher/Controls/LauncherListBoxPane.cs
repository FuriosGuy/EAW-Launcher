using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FocLauncher.Game;
using FocLauncher.Items;
using FocLauncher.Mods;
using FocLauncher.Profiles;
using FocLauncher.Threading;
using FocLauncher.Utilities;

namespace FocLauncher.Controls
{
    public sealed class LauncherListBoxPane : ContentControl
    {
        private readonly object _syncObj = new object();

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            "SelectedItem", typeof(LauncherItem), typeof(LauncherListBoxPane), new PropertyMetadata(default(LauncherItem)));

        public static readonly DependencyProperty ActiveGameProperty = DependencyProperty.Register(
            nameof(ActiveGame), typeof(IGame), typeof(LauncherListBoxPane),
            new PropertyMetadata(null, OnActiveGameChanged));

        public static readonly DependencyProperty FilterTextProperty = DependencyProperty.Register(
            nameof(FilterText), typeof(string), typeof(LauncherListBoxPane),
            new PropertyMetadata(string.Empty, OnFilterTextChanged));

        public LauncherItem SelectedItem
        {
            get => (LauncherItem) GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public IGame? ActiveGame
        {
            get => (IGame?) GetValue(ActiveGameProperty);
            set => SetValue(ActiveGameProperty, value);
        }

        public string FilterText
        {
            get => (string) GetValue(FilterTextProperty);
            set => SetValue(FilterTextProperty, value);
        }


        private bool _initializedItemManagerEvents;
        private LauncherListBoxItem? _dropTargetContainer;
        private HashSet<string> _profileModKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


        private readonly ObservableCollection<ILauncherItem> _itemCollection = new ObservableCollection<ILauncherItem>();

        public ReadOnlyObservableCollection<ILauncherItem> ItemCollection { get; }

        public ICollectionView ItemView { get; }

        internal event EventHandler? ModStateChanged;

        internal LauncherListBox ListBox { get; }

        private LauncherItemManager ItemManager => LauncherItemManager.Instance;

        public LauncherListBoxPane()
        {
            UseLayoutRounding = false;
            Focusable = true;
            FocusVisualStyle = null;
            ListBox = new LauncherListBox();
            ListBox.Background = System.Windows.Media.Brushes.Transparent;
            ListBox.BorderThickness = new Thickness(0);
            ListBox.AllowDrop = true;
            ListBox.DragOver += OnListBoxDragOver;
            ListBox.DragLeave += OnListBoxDragLeave;
            ListBox.Drop += OnListBoxDrop;
            ItemCollection = new ReadOnlyObservableCollection<ILauncherItem>(_itemCollection);
            ItemView = new ListCollectionView(_itemCollection);
            ItemView.Filter = IsVisibleItem;
            CreateBindings();
        }


        private void CreateBindings()
        {
            BindingOperations.SetBinding(ListBox, ItemsControl.ItemsSourceProperty,
                new Binding(nameof(ItemView)) { Source = this });

            BindingOperations.SetBinding(this, SelectedItemProperty,
                new Binding(nameof(ListBox.SelectedItem)) {Source = ListBox});
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            InitializeItemManagerEvents();
            Content = ListBox;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            if (e.OriginalSource != this || !(Content is UIElement content))
                return;
            FocusHelper.MoveFocusInto(content);
        }

        public bool AddGame(IGame game, bool changeSelection = true)
        {
            var result = false;
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                result = await AddGameAsync(game, changeSelection);
            });
            return result;
        }

        public async Task<bool> AddGameAsync(IGame game, bool changeSelection = true)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var item = ItemManager.GetGameObjectItem(game);

            lock (_syncObj)
            {
                var position = GetGamePosition();
                _itemCollection.Insert(position, item);
            }

            if (changeSelection)
                ListBox.SelectedItem = item;
            return true;
        }

        public IReadOnlyList<IMod> GetLoadSelectedMods(IPetroglyhGameableObject selectedGameObject)
        {
            var game = selectedGameObject as IGame ?? (selectedGameObject as IMod)?.Game;
            if (game is null)
                return Array.Empty<IMod>();

            return _itemCollection
                .OfType<LauncherItem>()
                .Where(item => item.IsLoadSelected && item.GameObject is IMod)
                .Select(item => (IMod)item.GameObject)
                .Where(mod => Equals(mod.Game, game))
                .Reverse()
                .ToList();
        }

        internal IReadOnlyList<LauncherItem> GetModsForActiveGame()
        {
            if (ActiveGame is null)
                return Array.Empty<LauncherItem>();

            return _itemCollection
                .OfType<LauncherItem>()
                .Where(IsModForActiveGame)
                .ToList();
        }

        internal IReadOnlyList<LauncherItem> GetPresetModsForActiveGame()
        {
            return GetModsForActiveGame()
                .Where(item => _profileModKeys.Contains(item.ModKey))
                .ToList();
        }

        internal IReadOnlyList<LauncherItem> GetAvailableModsForActiveGame(IEnumerable<string> profileModKeys)
        {
            var existingKeys = new HashSet<string>(profileModKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return GetModsForActiveGame()
                .Where(item => !existingKeys.Contains(item.ModKey))
                .ToList();
        }

        internal IReadOnlyList<string> GetPresetModKeys()
        {
            return GetPresetModsForActiveGame()
                .Select(item => item.ModKey)
                .Where(key => !string.IsNullOrEmpty(key))
                .ToList();
        }

        internal int GetSelectedModCount()
        {
            return GetPresetModsForActiveGame().Count;
        }

        internal void ApplyProfile(IReadOnlyList<string> profileModKeys)
        {
            var orderedProfileKeys = profileModKeys ?? Array.Empty<string>();
            _profileModKeys = new HashSet<string>(orderedProfileKeys, StringComparer.OrdinalIgnoreCase);
            var mods = GetModsForActiveGame();
            foreach (var item in mods)
                item.IsLoadSelected = _profileModKeys.Contains(item.ModKey);

            var orderedItems = orderedProfileKeys
                .Select(key => mods.FirstOrDefault(item => string.Equals(item.ModKey, key, StringComparison.OrdinalIgnoreCase)))
                .Where(item => item != null)
                .Cast<LauncherItem>()
                .Concat(mods.Where(item => !_profileModKeys.Contains(item.ModKey)))
                .ToList();

            var targetPositions = mods.Select(_itemCollection.IndexOf).OrderBy(index => index).ToList();
            for (var index = 0; index < orderedItems.Count && index < targetPositions.Count; index++)
            {
                var currentIndex = _itemCollection.IndexOf(orderedItems[index]);
                if (currentIndex >= 0 && currentIndex != targetPositions[index])
                    _itemCollection.Move(currentIndex, targetPositions[index]);
            }

            ItemView.Refresh();
            ListBox.SelectedItem = null;
        }

        internal void MoveMod(LauncherItem item, LauncherItem targetItem, bool insertAfter)
        {
            if (!CanMoveMod(item, targetItem))
                return;

            var currentIndex = _itemCollection.IndexOf(item);
            var targetIndex = _itemCollection.IndexOf(targetItem);
            var insertIndex = targetIndex + (insertAfter ? 1 : 0);

            if (currentIndex < insertIndex)
                insertIndex--;

            if (currentIndex == insertIndex)
                return;

            _itemCollection.Move(currentIndex, insertIndex);
            ListBox.SelectedItem = item;
            RaiseModStateChanged();
        }

        private void OnListBoxDragOver(object sender, DragEventArgs e)
        {
            var sourceItem = GetDraggedItem(e);
            var targetContainer = GetTargetContainer(e.OriginalSource as DependencyObject);
            var targetItem = targetContainer?.DataContext as LauncherItem;
            if (sourceItem is null || targetContainer is null || targetItem is null || !CanMoveMod(sourceItem, targetItem))
            {
                ClearDropTarget();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var dropPoint = e.GetPosition(targetContainer);
            var insertAfter = dropPoint.Y >= targetContainer.ActualHeight / 2;
            MoveMod(sourceItem, targetItem, insertAfter);
            SetDropTarget(targetContainer, insertAfter);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnListBoxDragLeave(object sender, DragEventArgs e)
        {
            ClearDropTarget();
        }

        private void OnListBoxDrop(object sender, DragEventArgs e)
        {
            var sourceItem = GetDraggedItem(e);
            var targetContainer = GetTargetContainer(e.OriginalSource as DependencyObject);
            var targetItem = targetContainer?.DataContext as LauncherItem;
            if (sourceItem is null || targetItem is null || targetContainer is null || !CanMoveMod(sourceItem, targetItem))
            {
                ClearDropTarget();
                return;
            }

            var dropPoint = e.GetPosition(targetContainer);
            ClearDropTarget();
            MoveMod(sourceItem, targetItem, dropPoint.Y >= targetContainer.ActualHeight / 2);
            e.Handled = true;
        }

        private static LauncherItem? GetDraggedItem(DragEventArgs e)
        {
            return e.Data.GetDataPresent(typeof(LauncherItem))
                ? e.Data.GetData(typeof(LauncherItem)) as LauncherItem
                : null;
        }

        private static LauncherListBoxItem? GetTargetContainer(DependencyObject? source)
        {
            if (source is LauncherListBoxItem item)
                return item;

            if (!(source is Visual visual))
                return null;

            return visual.FindAncestor<LauncherListBoxItem>();
        }

        private bool CanMoveMod(LauncherItem sourceItem, LauncherItem targetItem)
        {
            if (ReferenceEquals(sourceItem, targetItem) ||
                !(sourceItem.GameObject is IMod sourceMod) ||
                !(targetItem.GameObject is IMod targetMod) ||
                !Equals(sourceMod.Game, targetMod.Game))
                return false;

            return _itemCollection.IndexOf(sourceItem) >= 0 && _itemCollection.IndexOf(targetItem) >= 0;
        }

        private void SetDropTarget(LauncherListBoxItem targetContainer, bool insertAfter)
        {
            if (_dropTargetContainer != targetContainer)
            {
                ClearDropTarget();
                _dropTargetContainer = targetContainer;
            }

            targetContainer.DropInsertAfter = insertAfter;
            targetContainer.IsDropTarget = true;
        }

        private void ClearDropTarget()
        {
            if (_dropTargetContainer is null)
                return;

            _dropTargetContainer.IsDropTarget = false;
            _dropTargetContainer.DropInsertAfter = false;
            _dropTargetContainer = null;
        }

        private int GetGamePosition()
        {
            return ItemCollection.Count;
        }

        private static void OnActiveGameChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            var pane = (LauncherListBoxPane) obj;
            pane.ItemView.Refresh();
            pane.ListBox.SelectedItem = null;
        }

        private static void OnFilterTextChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            ((LauncherListBoxPane) obj).ItemView.Refresh();
        }

        private bool IsVisibleItem(object value)
        {
            if (!(value is LauncherItem item) || !IsModForActiveGame(item) || !_profileModKeys.Contains(item.ModKey))
                return false;

            if (string.IsNullOrWhiteSpace(FilterText))
                return true;

            var filter = FilterText.Trim();
            return item.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.ModKey.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.ModSource.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsModForActiveGame(LauncherItem item)
        {
            return ActiveGame != null && item.GameObject is IMod mod && Equals(mod.Game, ActiveGame);
        }

        private void RaiseModStateChanged()
        {
            ModStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void InitializeItemManagerEvents()
        {
            if (!_initializedItemManagerEvents)
            {
                _initializedItemManagerEvents = true;
                var itemManager = ItemManager;
                itemManager.OnItemAdded += OnItemAdded;
            }
        }

        private void OnItemAdded(object sender, LauncherItemEventArgs e)
        {
            if (e.Item.GameObject is IGame game)
                AddGame(game, false);
            else if (e.Item.GameObject is IMod mod)
            {
                lock (_syncObj)
                {
                    var gameItem = ItemManager.TryGetItem(mod.Game);
                    if (gameItem is null)
                        return;
                    var insertPos = GetNextGamePos(gameItem) -1;
                    lock (_syncObj) 
                        _itemCollection.Insert(insertPos, ItemManager.GetGameObjectItem(mod));
                    if (ItemManager.TryGetItem(mod) is LauncherItem item)
                        item.PropertyChanged += OnLauncherItemPropertyChanged;
                }
            }
        }

        private void OnLauncherItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LauncherItem.IsLoadSelected))
                RaiseModStateChanged();
            else if (e.PropertyName == nameof(LauncherItem.Text))
                ItemView.Refresh();
        }

        private int GetNextGamePos(ILauncherItem currentItem)
        {
            var currentGamePos = ItemCollection.IndexOf(currentItem);
            var temp = ItemCollection.Skip(currentGamePos + 1);
            var nextGame = temp.FirstOrDefault(x => x.GameObject is IGame);
            if (nextGame == null)
                return currentGamePos + 1;
            return ItemCollection.IndexOf(nextGame) + 1;
        }
    }
}
