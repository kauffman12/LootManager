using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using log4net;
using System;
using System.Windows.Data;
using System.Windows.Threading;
using System.Globalization;

namespace LootManager
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    private static readonly ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    private ObservableCollection<LootedListItem> lootedListItems = new ObservableCollection<LootedListItem>();
    private ObservableCollection<WatchListItem> watchListItems = new ObservableCollection<WatchListItem>();
    private IDictionary<string, ObservableCollection<RequestListItem>> requestListMap = new Dictionary<string, ObservableCollection<RequestListItem>>();
    private GridLength AUTO_GRID = (GridLength) new GridLengthConverter().ConvertFromString("Auto");
    private GridLength STAR_GRID = (GridLength)new GridLengthConverter().ConvertFromString("*");
    private SimpleChatController officerChatController;
    private GuildChatController guildChatController;
    private TellsChatController tellsChatController;
    private Task textUpdateTask = null;
    private LootAuditWindow lootAuditWindow = null;
    private bool ignoreOneEvent = false;
    private Brush EXPIRED_ROW_COLOR = new SolidColorBrush(Colors.DarkRed);

    public MainWindow()
    {
      InitializeComponent();

      // read properties file
      RuntimeProperties.deserialize();

      try
      {
        double height = System.Double.Parse(RuntimeProperties.getProperty("height"));
        double width = System.Double.Parse(RuntimeProperties.getProperty("width"));
        if (height > 200 && width > 200)
        {
          Height = height;
          Width = width;
        }

        // first grid length position
        string firstGridLength = RuntimeProperties.getProperty("first_grid_length");
        if (firstGridLength != null)
        {
          mainGrid.ColumnDefinitions[0].Width = (GridLength) new GridLengthConverter().ConvertFromString(firstGridLength);
        }       
      }
#pragma warning disable CS0168 // Variable is declared but never used
      catch (System.Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
      {
        // do nothing
      }

      // new loot config
      newLootDate.SelectedDate = System.DateTime.Today;
      resetNewLoot(false, false);

      lootedListView.ItemsSource = lootedListItems;
      watchListView.ItemsSource = watchListItems;
      watchListItems.CollectionChanged += WatchListItems_CollectionChanged;

      // chat controllers
      officerChatController = new SimpleChatController(officerChatBox, autoScroll);
      guildChatController = new GuildChatController(guildChatBox, watchListView, lootChatOnly, autoScroll);
      tellsChatController = new TellsChatController(tellsChatBox, watchListView, requestListMap, lootChatOnly, autoScroll);

      // add log event handler
      LogReader.getInstance().logEvent += new LogReader.LogReaderEvent(logReaderEventHandler);

      // read log file if one set
      setLogFile(RuntimeProperties.getProperty("log_file"), 0);

      // tasks to run on an interval
      DispatcherTimer timer = new DispatcherTimer();
      timer.Tick += new EventHandler(Timer_Tick);
      timer.Interval = new TimeSpan(0, 0, 60);
      timer.Start();
    }

    private void Timer_Tick(object sender, EventArgs e)
    {
      lock (requestListView)
      {
        // check if items have expired
        ObservableCollection<RequestListItem> list = requestListView.ItemsSource as ObservableCollection<RequestListItem>;
        if (list != null)
        {
          list.ToList().ForEach(item => validateRequestItem(item));
        }
      }
    }

    //
    // Shutdown Related
    //
    private void Application_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
      // stop reader if active
      LogReader.getInstance().stop();
      RuntimeProperties.setProperty("height", Height.ToString());
      RuntimeProperties.setProperty("width", Width.ToString());
      RuntimeProperties.setProperty("first_grid_length", mainGrid.ColumnDefinitions[0].Width.ToString());
      RuntimeProperties.serialize();
    }

    private void Window_Closed(object sender, System.EventArgs e)
    {
      Application.Current.Shutdown();
    }

    //
    // Main Menu Actions
    //
    private void MenuItemExit_Click(object sender, RoutedEventArgs e)
    {
      // shutdown application
      Application.Current.Shutdown();
    }

    private void MenuItemConnect_Click(object sender, RoutedEventArgs e)
    {
      Title = Title.Replace("Not Connected", "Connecting...");

      new Thread(() =>
      {
        // Authenticate to Google Drive
        TokenManager.authenticate();

        Dispatcher.BeginInvoke((System.Action)(() =>
        {
          Title = Title.Replace("Connecting...", "Loading Database...");
        }));

        DataManager.load();

        Dispatcher.BeginInvoke((System.Action)(() =>
        {
          connectMenuItem.IsEnabled = false;
          disconnectMenuItem.IsEnabled = refreshMenuItem.IsEnabled = true;
          Title = Title.Replace("Loading Database...", "Connected");
          resetNewLoot(true, true);
          checkLoadLootHistory();
          loadMembers();
        }));
      }).Start();
    }

    private void MenuItemDisconnect_Click(object sender, RoutedEventArgs e)
    {
      TokenManager.cleanup();
      DataManager.cleanup();
      connectMenuItem.IsEnabled = true;
      disconnectMenuItem.IsEnabled = refreshMenuItem.IsEnabled = false;
      Title = Title.Replace("Connecting...", "Not Connected");
      Title = Title.Replace("Connected", "Not Connected");
    }

    private void MenuItemRefresh_Click(object sender, RoutedEventArgs e)
    {
      Title = Title.Replace("Connecting...", "Reloading Database...");
      Title = Title.Replace("Connected", "Reloading Database...");
      DataManager.cleanup();
      DataManager.load();
      resetNewLoot(true, true);
      checkLoadLootHistory();
      loadMembers();
      Title = Title.Replace("Reloading Database...", "Connected");
    }

    private void MenuItemSelectEQLogFile_Click(object sender, RoutedEventArgs e)
    {
      loadEQLogFile(0);
    }

    private void MenuItemSelectEQLogFile1_Click(object sender, RoutedEventArgs e)
    {
      loadEQLogFile(60);
    }

    private void MenuItemSelectEQLogFile2_Click(object sender, RoutedEventArgs e)
    {
      loadEQLogFile(120);
    }

    private void MenuItemSelectEQLogFile4_Click(object sender, RoutedEventArgs e)
    {
      loadEQLogFile(240);
    }

    private void MenuItemSelectEQLogFile6_Click(object sender, RoutedEventArgs e)
    {
      loadEQLogFile(360);
    }

    private void loadEQLogFile(int mins)
    {
      // WPF doesn't have its own file chooser so use Win32 version
      Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

      // filter to txt files
      dlg.DefaultExt = ".txt";
      dlg.Filter = "eqlog_player_server (.txt)|*.txt";

      // show dialog and read result
      // if null result then dialog was probably canceled
      System.Nullable<bool> result = dlg.ShowDialog();
      if (result == true)
      {
        if (mins > 0)
        {
          resetNewLoot(true, true);
          tellsChatController.Clear();
          guildChatController.Clear();
          officerChatController.Clear();
          lootedListItems.Clear();
          watchListItems.Clear();
          requestListView.ItemsSource = null;
          requestListMap.Clear();
        }

        setLogFile(dlg.FileName, mins);
      }
    }

    private void ListClear_Click(object sender, RoutedEventArgs e)
    {
      if (sender == watchListClearMenuItem && watchListView.SelectedItem != null)
      {
        watchListItems.Remove(watchListView.SelectedItem as WatchListItem);
      }
      else if (sender == lootedListClearMenuItem && lootedListView.SelectedItem != null)
      {
        lootedListItems.Remove(lootedListView.SelectedItem as LootedListItem);
      }
      else if (sender == requestListClearMenuItem && requestListView.SelectedItems.Count > 0)
      {
        requestListView.SelectedItems.Cast<RequestListItem>().ToList().Where(selected => requestListMap.ContainsKey(selected.Item)).ToList()
        .ForEach(selected => requestListMap[selected.Item].Remove(selected));
      }
      else if (sender == membersListViewClearMenuItem && membersListView.SelectedItem != null)
      {
        ObservableCollection<Player> list = membersListView.ItemsSource as ObservableCollection<Player>;
        Player player = membersListView.SelectedItem as Player;
        if (player != null && list != null)
        {
          list.Remove(player);
        }
      }
    }

    //
    // Looted Item Table Actions
    //
    private void LootedListContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
      lootedListClearMenuItem.IsEnabled = lootedListView.SelectedIndex > -1;
      lootedListClearAllMenuItem.IsEnabled = lootedListView.Items.Count > 0;
    }

    private void LootedListReset_Click(object sender, RoutedEventArgs e)
    {
      lootedListItems.Clear();
    }

    //
    // Requested Loot Table Actions
    //
    private void RequestListContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
      requestListClearMenuItem.IsEnabled = requestListView.SelectedIndex > -1;
      requestListClearAllMenuItem.IsEnabled = requestListView.Items.Count > 0;
      requestListViewLootMenuItem.IsEnabled = requestListView.Items.Count > 0;
      requestListClearExpiredMenuItem.IsEnabled = requestListView.Items.Cast<RequestListItem>().Any(item => validateRequestItem(item));
    }

    private void RequestListReset_Click(object sender, RoutedEventArgs e)
    {
      if (watchListView.SelectedIndex >= 0)
      {
        WatchListItem selected = watchListView.SelectedItem as WatchListItem;
        if (selected != null && requestListMap.ContainsKey(selected.Item))
        {
          requestListMap[selected.Item].Clear();
        }
      }
    }

    private void RequestListViewLoot_Click(object sender, RoutedEventArgs e)
    {
      ObservableCollection<RequestListItem> list = requestListView.ItemsSource as ObservableCollection<RequestListItem>;
      if (list != null)
      {
        List<string> players = list.Select(item => item.Player).ToList();
        viewLoot(players);
      }
    }

    private void RequestListViewClearExpired_Click(object sender, RoutedEventArgs e)
    {
      lock (requestListView)
      {
        requestListView.Items.Cast<RequestListItem>().ToList().Where(selected => validateRequestItem(selected)).ToList()
          .ForEach(selected => requestListMap[selected.Item].Remove(selected));
      }
    }

    private void viewLoot(List<string> players)
    {
      string result = string.Join(" ", players);
      if (result != null && result.Length > 3)
      {
        lootHistoryTab.Focus();
        lootDetailsFilterBox.FontStyle = FontStyles.Normal;
        lootDetailsFilterBox.Text = result;
      }
    }

    private void RequestList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
      generateOfficerChatMessage();
    }

    //
    // Watch List Table Actions
    //
    private void WatchListItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
      if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems.Count >= 0)
      {
        foreach (object item in e.NewItems)
        {
          WatchListItem watchListItem = item as WatchListItem;
          if (watchListItem != null && !requestListMap.ContainsKey(watchListItem.Item))
          {
            ObservableCollection<RequestListItem> requestList = new ObservableCollection<RequestListItem>();
            requestListMap.Add(watchListItem.Item, requestList);
            requestList.CollectionChanged += RequestList_CollectionChanged;
          }
        }
      }
    }

    private void WatchListContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
      watchListClearMenuItem.IsEnabled = watchListRenameMenuItem.IsEnabled = watchListView.SelectedIndex > -1;
      watchListClearAllMenuItem.IsEnabled = watchListView.Items.Count > 0;
    }

    private void WatchList_UnloadingRow(object sender, DataGridRowEventArgs e)
    {
      requestListView.ItemsSource = null;
    }

    private void WatchListRename_Click(object sender, RoutedEventArgs e)
    {
      if (watchListView.SelectedIndex >= 0)
      {
        watchListView.CurrentCell = new DataGridCellInfo(watchListView.SelectedItem, watchListView.Columns[1]);
        watchListView.BeginEdit();
      }
    }

    private void WatchListReset_Click(object sender, RoutedEventArgs e)
    {
      requestListView.ItemsSource = null;
      requestListMap.Clear();
      watchListItems.Clear();
    }

    private void WatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (watchListView.SelectedIndex >= 0)
      {
        WatchListItem selected = watchListView.SelectedItem as WatchListItem;
        if (selected != null && requestListMap.ContainsKey(selected.Item))
        {
          requestListView.ItemsSource = requestListMap[selected.Item];
        }
      }
      else
      {
        requestListView.ItemsSource = null;
      }

      generateOfficerChatMessage();
    }

    //
    // Generated Officer Chat Related Actions
    //
    private void CheckBoxLootOnly_Checked(object sender, RoutedEventArgs e)
    {
      guildChatController.toggleDisplayLootOnly();
      tellsChatController.toggleDisplayLootOnly();
    }

    private void GenChatBox_GotFocus(object sender, RoutedEventArgs e)
    {
      if (genChatBox.FontStyle == FontStyles.Italic)
      {
        genChatBox.Clear();
        genChatBox.FontStyle = FontStyles.Normal;
      }
    }

    private void GenChatBox_LostFocus(object sender, RoutedEventArgs e)
    {
      if (genChatBox.FontStyle == FontStyles.Normal && genChatBox.Text.Length == 0)
      {
        resetGenChatBox();
      }
    }

    //
    // Chat Windows Checkbox Actions
    //
    private void CheckBoxAutoscroll_Checked(object sender, RoutedEventArgs e)
    {
      // if switched to checked
      if (autoScroll.IsChecked.Value)
      {
        guildChatBox.ScrollToEnd();
        officerChatBox.ScrollToEnd();
        tellsChatBox.ScrollToEnd();
      }
    }

    private void CheckBoxLootAllTypes_Checked(object sender, RoutedEventArgs e)
    {
      generateOfficerChatMessage();
    }

    // 
    // Helper methods
    //
    private void setLogFile(string path, int seconds)
    {
      if (path != null)
      {
        LogReader.getInstance().setLogFile(path, seconds);
        chatBorder.Background = new SolidColorBrush(Color.FromRgb(179, 220, 217));

        logStatusText.FontStyle = FontStyles.Normal;
        logStatusText.ToolTip = path;
        logStatusText.Content = "Log Selected (" + Path.GetFileName(path) + ")";
      }
    }

    private void logReaderEventHandler(object sender, LogEventArgs e)
    {
      Dispatcher.BeginInvoke((System.Action)(() =>
      {
        switch (e.type)
        {
          case LogReader.LOG_TYPE.OFFICER_CHAT:
            officerChatController.handleEvent(e);
            break;
          case LogReader.LOG_TYPE.GUILD_CHAT:
            guildChatController.handleEvent(e);
            break;
          case LogReader.LOG_TYPE.TELLS:
            tellsChatController.handleEvent(e);
            break;
          case LogReader.LOG_TYPE.LOOT:
            // Group 0 is everything       Group 1     Group 2     Group 3
            //[Sun May 13 20:20:29 2018] --(You) have (looted) (a|an) (Enchanted Runestone).--
            if (e.matches[0].Groups.Count == 5)
            {
              string item = e.matches[0].Groups[4].Value;
              string player = e.matches[0].Groups[1].Value;
              if ("You".Equals(player))
              {
                player = RuntimeProperties.getProperty("player");
                if (player == null)
                {
                  player = "Unknown";
                }
              }

              string found = "No";
              bool readyToGo = false;
              string slot = "";

              object foundItem = DataManager.findItem(item);
              if (foundItem != null && foundItem.GetType() == typeof(Item))
              {
                found = "Yes";
                Item theItem = (foundItem as Item);
                item = theItem.Name;
                slot = theItem.Slot;

                if (theItem.Slot.Length > 0 && theItem.EventName.Length > 0 && DataManager.getActivePlayerList().Any(pl => pl.Name.Equals(player)))
                {
                  readyToGo = true;
                }
              }

              LootedListItem looted = new LootedListItem { Player = player, Item = item, Slot = slot, Found = found, Ready = readyToGo };
              lootedListItems.Add(looted);
            }
            break;
        }
      }));
    }

    private void generateOfficerChatMessage()
    {
      string chat = "";
      ObservableCollection<RequestListItem> requestList = requestListView.ItemsSource as ObservableCollection<RequestListItem>;
      if (requestList != null && requestList.Count > 0)
      {
        string item = requestList[0].Item;
        chat = item;

        List<string> mainMembersProcess = new List<string>();
        List<string> altMembersProcess = new List<string>();
        List<string> rotMembersProcess = new List<string>();
        List<string> appsProcess = new List<string>();
        foreach (RequestListItem requestItem in requestList)
        {
          bool alt = false;
          bool rot = false;
          if ((System.DateTime.Now - requestItem.Added).TotalSeconds < 600)
          {
            string displayName = requestItem.Player;
            if (!"Main".Equals(requestItem.Type, StringComparison.OrdinalIgnoreCase))
            {
              alt = "Alt".Equals(requestItem.Type, StringComparison.OrdinalIgnoreCase);
              rot = "Rot".Equals(requestItem.Type, StringComparison.OrdinalIgnoreCase);
              displayName += "(" + requestItem.Type + ")";
            }

            if (DataManager.isMember(requestItem.Player))
            {
              if (!alt && !rot)
              {
                mainMembersProcess.Add(displayName);
              }
              else if (alt)
              {
                altMembersProcess.Add(displayName);
              }
              else if (rot)
              {
                rotMembersProcess.Add(displayName);
              }
            }
            else
            {
              appsProcess.Add(displayName);
            }
          }
        }

        List<string> process;
        if (lootAllTypes.IsChecked.Value)
        {
          process = mainMembersProcess;
          process.AddRange(rotMembersProcess);
          process.AddRange(appsProcess);
          process.AddRange(altMembersProcess);
        }
        else
        {
          process = mainMembersProcess.Count > 0 ? mainMembersProcess : rotMembersProcess.Count > 0 ? rotMembersProcess : appsProcess.Count > 0 ? appsProcess : altMembersProcess;
        }
        
        foreach (string displayName in process)
        {
          chat += " - " + displayName;
        }
      }

      if ("".Equals(chat))
      {
        resetGenChatBox();
      }
      else
      {
        genChatBox.FontStyle = FontStyles.Normal;
        genChatBox.Text = chat;
      }
    }

    private void RequestList_LoadingRow(object sender, DataGridRowEventArgs e)
    {
      validateRequestItem(e.Row.Item as RequestListItem);
    }

    private bool validateRequestItem(RequestListItem item)
    {
      bool expired = false;

      if (item != null && (System.DateTime.Now - item.Added).TotalSeconds > 600)
      {
        DataGridRow row = (DataGridRow) requestListView.ItemContainerGenerator.ContainerFromItem(item);
        if (row != null && row.Foreground != EXPIRED_ROW_COLOR)
        {
          row.Foreground = EXPIRED_ROW_COLOR;
          row.FontStyle = FontStyles.Italic;
          row.ToolTip = "Item Request is too old";
          generateOfficerChatMessage();
        }

        expired = true;
      }

      return expired;
    }

    private void resetGenChatBox()
    {
      WatchListItem item = watchListView.SelectedItem as WatchListItem;
      if (item != null)
      {
        genChatBox.FontStyle = FontStyles.Normal;
        genChatBox.Text = item.Item + " - ROT";
      }
      else if (genChatBox.FontStyle != FontStyles.Italic)
      {
        genChatBox.FontStyle = FontStyles.Italic;
        genChatBox.Text = "Suggested Officer Chat for Selected Item";
      }
    }

    private void LootedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (lootedListView.SelectedIndex >= 0)
      {
        LootedListItem listItem = lootedListView.SelectedItem as LootedListItem;
        ObservableCollection<Player> players = newLootPlayer.ItemsSource as ObservableCollection<Player>;
        if (players != null)
        {
          newLootPlayer.SelectedIndex = players.ToList().FindIndex(player => player.Name.Equals(listItem.Player));
        }

        if (newLootPlayer.SelectedIndex == -1)
        {
          newLootPlayer.Text = listItem.Player;
        }

        List<Item> found = DataManager.getItemsList().Where(item => item.Name.Equals(listItem.Item)).ToList();
        if (found.Count > 0)
        {
          newLootItem.ItemsSource = found;
          if (newLootItem.SelectedIndex != 0)
          {
            newLootItem.SelectedIndex = 0;
          }
        }
        else
        {
          newLootItem.Text = listItem.Item;
          newLootSlot.Text = "";
          resetNewLoot(false, false);
        }
      }
    }

    private void NewLootPlayer_LostFocus(object sender, RoutedEventArgs e)
    {
      resetNewLoot(false, false);
    }

    private void NewLootItem_LostFocus(object sender, RoutedEventArgs e)
    {
      resetNewLoot(false, false);
    }

    private void NewLootSlot_LostFocus(object sender, RoutedEventArgs e)
    {
      resetNewLoot(false, false);
    }

    private void NewLootEvent_LostFocus(object sender, RoutedEventArgs e)
    {
      resetNewLoot(false, false);
    }

    private void NewLootReset_Click(object sender, RoutedEventArgs e)
    {
      resetNewLoot(true, true);
    }

    private void NewLoot_TextChanged(object sender,TextChangedEventArgs e)
    {
      checkSaveEnabled();
    }

    private void checkSaveEnabled()
    {
      if (textUpdateTask == null || textUpdateTask.IsCompleted)
      {
        textUpdateTask = Task.Delay(System.TimeSpan.FromMilliseconds(100)).ContinueWith(task =>
        {
          Dispatcher.BeginInvoke((System.Action)(() =>
          {
            newLootSaveButton.IsEnabled = (newLootSlot.SelectedIndex > 0 && newLootEvent.Text.Length > 0 && !newLootEvent.Text.Equals("Select Event") &&
            newLootItem.Text.Length > 0 && !newLootItem.Text.Equals("Select Item") && !newLootItem.Text.Equals("No Matching Loot Found") && newLootPlayer.Text.Length > 0 && !newLootPlayer.Text.Equals("Select Player"));

            updateItemsDB.IsEnabled = (newLootItem.Text.Length > 0 && !newLootItem.Text.Equals("Select Item") && !newLootItem.Text.Equals("No Matching Loot Found") &&  newLootItem.SelectedIndex == -1);
            updateItemsDB.Visibility = updateItemsDB.IsEnabled ? Visibility.Visible : Visibility.Hidden;
            newLootItem.Foreground = updateItemsDB.IsEnabled ? new SolidColorBrush(Colors.DarkOrange) : new SolidColorBrush(Colors.Black);
          }));
        });
      }
    }

    private void NewLootItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      Item item;
      if (newLootItem.SelectedIndex >= 0 && (item = newLootItem.SelectedItem as Item) != null)
      {
        List<string> slots = newLootSlot.ItemsSource as List<string>;
        if (slots != null)
        {
          int index = slots.IndexOf(item.Slot);
          if (index >= 0 && newLootSlot.SelectedIndex != index)
          {
            ignoreOneEvent = true;
            newLootSlot.SelectedIndex = index;
          }
        }

        List<RaidEvent> events = newLootEvent.ItemsSource as List<RaidEvent>;
        if (!"Any".Equals(item.EventName))
        {
          int eventIndex = events.FindIndex(evt => evt.ShortName.Equals(item.EventName));
          if (newLootEvent.SelectedIndex != eventIndex)
          {
            newLootEvent.SelectedIndex = eventIndex;
          }
        }
      }
      else
      {
        resetNewLoot(false, false);
      }

      checkSaveEnabled();
    }

    private void NewLootSlot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (newLootEvent == null || ignoreOneEvent)
      {
        ignoreOneEvent = false;
        return; // UI not ready?
      }

      IEnumerable<Item> items = DataManager.getItemsList();

      RaidEvent theEvent = newLootEvent.SelectedItem as RaidEvent;
      if (theEvent != null)
      {
        items = items.Where(it => it.Tier.Equals(theEvent.Tier) || it.EventName.Equals(theEvent.ShortName));
      }

      string slot = newLootSlot.SelectedItem as string;
      if (slot != null && (newLootSlot.SelectedIndex >= 1))
      {
        items = items.Where(it => slot.Equals(it.Slot)).ToList();
      }

      updateItems(items);
      resetNewLoot(false, false);
      checkSaveEnabled();
    }

    private void NewLootEvent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      IEnumerable<Item> items = DataManager.getItemsList();

      string slot = newLootSlot.SelectedItem as string;
      if (slot != null && newLootSlot.SelectedIndex > 0)
      {
        items = items.Where(it => slot.Equals(it.Slot));
      }

      if (newLootEvent.SelectedIndex >= 0)
      {
        RaidEvent theEvent = newLootEvent.SelectedItem as RaidEvent;
        if (theEvent != null)
        {
          items = items.Where(it => it.Tier.Equals(theEvent.Tier) || it.EventName.Equals(theEvent.ShortName));
        }
      }

      updateItems(items);
      resetNewLoot(false, false);
      checkSaveEnabled();
    }

    private void NewLootSave_Clicked(object sender, RoutedEventArgs e)
    {
      string player = newLootPlayer.Text;
      string item = newLootItem.Text;
      string slot = newLootSlot.Text;
      string eventName = newLootEvent.Text;
      string date = newLootDate.Text;
      string rot = newLootRot.IsChecked.Value ? "Yes" : "";
      string alt = newLootAlt.IsChecked.Value ? "Yes" : "";

      if (player.Length > 0 && item.Length > 0 && slot.Length > 0 && eventName.Length > 0 && date.Length > 0 &&
        !player.Equals("Select Player") && !slot.Equals("Any Slot") && !eventName.Equals("Select Event"))
      {
        string auditItemsDB = null;
        string auditLine = player + " | " + item + " | " + slot + " | " + eventName + " | " + date;

        if ("Yes".Equals(alt))
        {
          auditLine += " | " + "Alt";
        }

        if ("Yes".Equals(rot))
        {
          auditLine += " | " + "Rot";
        }

        try
        {
          DataManager.saveLoot(date, player, eventName, item, slot, rot, alt);

          auditLine = "S " + auditLine;

          LootedListItem listItem = lootedListView.SelectedItem as LootedListItem;
          if (listItem != null && listItem.Player.Equals(player) && listItem.Item.Equals(item))
          {
            lootedListItems.Remove(listItem);
          }

          resetNewLoot(true, false);

          // update ItemsDB if needed
          if (updateItemsDB.IsEnabled && updateItemsDB.IsChecked.Value)
          {
            auditItemsDB = slot + " | " + item + " | " + eventName;

            try
            {
              DataManager.saveItem(slot, item, eventName);
              auditItemsDB = "S " + auditItemsDB;
            }
            catch(System.Exception ex)
            {
              LOG.Error("Could not save ItemsDB", ex);
              auditItemsDB = "E " + auditItemsDB;
            }
          }
        }
        catch(System.Exception ex)
        {
          LOG.Error("Could not save Loot", ex);
          auditLine = "E " + auditLine;
        }

        if (newLootAuditTextBox.Text.Contains("Audit Log"))
        {
          newLootAuditTextBox.Clear();
          newLootAuditTextBox.AppendText(auditLine);
        }
        else
        {
          newLootAuditTextBox.AppendText("\r" + auditLine);
        }

        if (auditItemsDB != null)
        {
          newLootAuditTextBox.AppendText("\r" + auditItemsDB);
        }

        newLootAuditTextBox.ScrollToEnd();
      }
    }

    private void LootedList_LoadingRow(object sender, DataGridRowEventArgs e)
    {
      //LootedListItem theItem = e.Row.Item as LootedListItem;
      //if (theItem != null && theItem.Ready)
      //{
        //e.Row.Background = new SolidColorBrush(Colors.LightGreen);
      //}
    }

    private void resetNewLoot(bool force, bool dateToo)
    {
      if (force)
      {
        newLootPlayer.ItemsSource = DataManager.getActivePlayerList();
        newLootItem.ItemsSource = DataManager.getItemsList();
        newLootEvent.ItemsSource = DataManager.getEventsList();

        if (dateToo)
        {
          newLootDate.SelectedDate = System.DateTime.Today;
        }

        // clear out initial value used for testing
        if (newLootSlot.ItemsSource == null)
        {
          newLootSlot.Items.Clear();
        }

        newLootSlot.ItemsSource = DataManager.getArmorTypes();
        newLootSlot.SelectedIndex = 0;

        newLootAlt.IsChecked = false;
        newLootRot.IsChecked = false;
      }

      if (force || (newLootPlayer.SelectedIndex == -1 && newLootPlayer.Text.Trim().Length == 0))
      {
        newLootPlayer.SelectedIndex = -1;
        newLootPlayer.Text = "Select Player";
      }

      if (force || (newLootItem.SelectedIndex == -1 && newLootItem.Text.Trim().Length == 0))
      {
        newLootItem.SelectedIndex = -1;
        newLootItem.Text = "Select Item";
      }

      // uh hack to not change event on save (or unload row i guess)
      if ((force && dateToo) || (newLootEvent.SelectedIndex == -1 && newLootEvent.Text.Trim().Length == 0))
      {
        newLootEvent.SelectedIndex = -1;
        newLootEvent.Text = "Select Event";
      }
    }

    private void updateItems(IEnumerable<Item> items)
    {
      Item item = newLootItem.SelectedItem as Item;
      newLootItem.ItemsSource = items;

      if (item != null)
      {
        int index = items.ToList().IndexOf(item);
        if (index >= 0)
        {
          newLootItem.SelectedIndex = index;
        }
      }

      if (newLootItem.Items.Count == 1 && newLootItem.SelectedIndex == -1)
      {
        newLootItem.SelectedIndex = 0;
      }
      else if (newLootItem.Items.Count == 0)
      {
        newLootItem.Text = "No Matching Loot Found";
      }
      else
      {
        newLootItem.Text = "Select Item";
      }
    }

    private void NewLootAlt_Checked(object sender, RoutedEventArgs e)
    {
      if (!newLootRot.IsChecked.Value)
      {
        newLootRot.IsChecked = true;
      }
    }

    private void GenChatButton_Click(object sender, RoutedEventArgs e)
    {
      if (genChatBox.FontStyle != FontStyles.Italic)
      {
        Clipboard.SetDataObject(genChatBox.Text);
        copyNotice.Opacity = 1.0;
        copyNotice.Visibility = Visibility.Visible;
        Task.Delay(System.TimeSpan.FromMilliseconds(150)).ContinueWith(task => hideCopyNotice());
      }
    }

    private void hideCopyNotice()
    {
      Dispatcher.BeginInvoke((System.Action)(() =>
      {
        copyNotice.Opacity = copyNotice.Opacity - 0.10;
        if (copyNotice.Opacity > 0)
        {
          Task.Delay(System.TimeSpan.FromMilliseconds(50)).ContinueWith(task => hideCopyNotice());
        }
        else
        {
          copyNotice.Visibility = Visibility.Hidden;
        }
      }));
    }

    private void LootedList_UnloadingRow(object sender, DataGridRowEventArgs e)
    {
      resetNewLoot(true, false);
    }

    private void ChatLootTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (secondTabControl.SelectedItem == lootHistoryTab)
      {
        // workaround to getting help text displayed
        secondTabControl.Focus();
      }

      checkLoadLootHistory();
    }

    private void checkLoadLootHistory()
    {
      // load data firs time tab is used
      if (secondTabControl.SelectedItem == lootHistoryTab && lootDetailsListView.ItemsSource == null)
      {
        loadHistoryData();
      }
    }

    private void LootDetailsFilterTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
      if (lootDetailsFilterBox.FontStyle == FontStyles.Italic)
      {
        lootDetailsFilterBox.Clear();
        lootDetailsFilterBox.FontStyle = FontStyles.Normal;
      }
    }

    private void LootDetailsFilterTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
      if (lootDetailsFilterBox.FontStyle == FontStyles.Normal && lootDetailsFilterBox.Text.Length == 0)
      {
        lootDetailsFilterBox.FontStyle = FontStyles.Italic;
        lootDetailsFilterBox.Text = "Enter Player Names To Limit Results";

        Dispatcher.BeginInvoke((System.Action)(() =>
        {
            loadHistoryData();
        }
        ));
      }
    }

    private void LootDetailsFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
      if (textUpdateTask == null || textUpdateTask.IsCompleted)
      {
        textUpdateTask = Task.Delay(System.TimeSpan.FromMilliseconds(200)).ContinueWith(task =>
        {
          Dispatcher.BeginInvoke((System.Action)(() => loadHistoryData() ));
        });
      }
    }

    private void LootDetailsFilterTextBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key == System.Windows.Input.Key.Escape)
      {
        lootHistoryTab.Focus();
      }
    }

    private void TimeSpinner_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
      loadHistoryData();
    }

    private void TierComboBox_ItemSelectionChanged(object sender, Xceed.Wpf.Toolkit.Primitives.ItemSelectionChangedEventArgs e)
    {
      // silly workaround
      List<string> selected = tierComboBox.SelectedItemsOverride as List<string>;
      if (selected != null)
      {
        tierComboBox.SelectedValue = string.Join(",", selected);
      }

      loadHistoryData();
    }

    private void ClassComboBox_ItemSelectionChanged(object sender, Xceed.Wpf.Toolkit.Primitives.ItemSelectionChangedEventArgs e)
    {
      // silly workaround
      List<string> selected = classComboBox.SelectedItemsOverride as List<string>;
      if (selected != null)
      {
        classComboBox.SelectedValue = string.Join(",", selected);
      }

      loadHistoryData();
    }

    private void loadHistoryData()
    {
      // if data loaded
      if (DataManager.getTiers().Count > 0)
      {
        // if first time
        if (historyStatusText.FontStyle == FontStyles.Italic)
        {
          historyStatusText.FontStyle = FontStyles.Normal;
          historyBorder.Background = new SolidColorBrush(Color.FromRgb(179, 220, 217));
          tierComboBox.ItemsSource = DataManager.getTiers();
          tierComboBox.SelectedItemsOverride = DataManager.getCurrentTiers();
          classComboBox.ItemsSource = DataManager.getClassTypes();
          classComboBox.SelectedItemsOverride = DataManager.getClassTypes();
        }

        // get names filter
        List<string> names = null;
        if (lootDetailsFilterBox.FontStyle != FontStyles.Italic && lootDetailsFilterBox.Text.Length > 3)
        {
          if (lootDetailsFilterBox.Text.Contains(","))
          {
            names = lootDetailsFilterBox.Text.Split(',').ToList();
          }
          else
          {
            names = lootDetailsFilterBox.Text.Split(null).ToList();
          }
        }

        // get tiers filter
        List<string> tiers = tierComboBox.SelectedItemsOverride as List<string>;
        List<string> classTypes = classComboBox.SelectedItemsOverride as List<string>;
        lootDetailsListView.ItemsSource = DataManager.getLootDetails(names, tiers, classTypes, timeSpinner.Value);
        historyStatusText.Content = DataManager.getHistoryStatus();
      }
    }

    private void LootDetailsListRow_DoubleClick(object sender, RoutedEventArgs e)
    {
      DataGridRow row = e.Source as DataGridRow;
      viewLootLog(row.DataContext as LootDetailsListItem);
    }

    private void LootDetailsListViewLog_Click(object sender, RoutedEventArgs e)
    {
      List<LootDetailsListItem> list = lootDetailsListView.SelectedItems.Cast<LootDetailsListItem>().ToList();
      if (list != null)
      {
        list.ForEach(l => viewLootLog(l));
      }
    }

    private void viewLootLog(LootDetailsListItem details)
    {
      if (details != null)
      {
        if (lootAuditWindow == null)
        {
          lootAuditWindow = new LootAuditWindow(tierComboBox.SelectedItemsOverride as List<string>, timeSpinner.Value);
          lootAuditWindow.Closed += (object s, System.EventArgs e2) => lootAuditWindow = null;
        }

        lootAuditWindow.Show();

        if (lootAuditWindow.WindowState == WindowState.Minimized)
        {
          lootAuditWindow.WindowState = WindowState.Normal;
        }

        lootAuditWindow.Activate();
        lootAuditWindow.load(details.Player);
      }
    }

    private void loadMembers()
    {
      // may not be ready during init
      if (membersListView != null)
      {
        ObservableCollection<Player> list = onlyActiveMembers.IsChecked.Value ? DataManager.getActivePlayerList() : DataManager.getFullPlayerList();
        membersListView.ItemsSource = list;

        if (membersText.FontStyle == FontStyles.Italic)
        {
          membersDataBorder.Background = new SolidColorBrush(Color.FromRgb(179, 220, 217));
          membersText.FontStyle = FontStyles.Normal;
        }

        membersText.Content = "Found " + list.Count + " Members";
      }
    }

    private void CheckBoxMemberActiveData_Checked(object sender, RoutedEventArgs e)
    {
      if (membersListView.SelectedItem != null)
      {
        DataGridCell theCell = sender as DataGridCell;
        if (theCell != null)
        {
          Player player = theCell.DataContext as Player;
          if (player != null && player == membersListView.SelectedItem)
          {
            if (player.Active != e.RoutedEvent.Name.Equals("Checked"))
            {
              player.Active = e.RoutedEvent.Name.Equals("Checked");
              updateMember(player.Name, player);
            }
          }
        }
      }
    }

    private void MembersListView_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
      // checkbox has its own function
      string newValue = null;
      Binding binding = null;

      if (e.EditAction.ToString().Equals("Commit"))
      {
        if (e.EditingElement as TextBox != null)
        {
          newValue = ((TextBox)e.EditingElement).Text;
          binding = (Binding)((DataGridBoundColumn)e.Column).Binding;
        }
        else if (e.EditingElement as ComboBox != null)
        {
          newValue = ((ComboBox)e.EditingElement).SelectedValue.ToString();
          binding = (Binding)((DataGridComboBoxColumn)e.Column).SelectedValueBinding;
        }

        if (newValue != null && binding != null)
        {
          string field = binding.Path.Path;
          string oldValue = e.Row.DataContext.GetType().GetProperty(field).GetValue(e.Row.DataContext, null).ToString();

          if (!oldValue.Equals(newValue))
          {
            Player player = e.Row.DataContext as Player;
            string name = player.Name;
            e.Row.DataContext.GetType().GetProperty(field).SetValue(e.Row.DataContext, newValue);

            if (!"Unknown".Equals(player.Name, StringComparison.OrdinalIgnoreCase) &&
              !"Unknown".Equals(player.ForumName, StringComparison.OrdinalIgnoreCase) &&
              !"Unknown".Equals(player.Class, StringComparison.OrdinalIgnoreCase))
            {
              updateMember(name, player);
            }
          }
        }
      }
    }

    private void MembersListViewAdd_Click(object sender, RoutedEventArgs e)
    {
      ObservableCollection<Player> list = membersListView.ItemsSource as ObservableCollection<Player>;

      if (list != null)
      {
        Player player = new Player { Name = "Unknown", ForumName = "Uknown", Class = "Unknown", Rank = "App", Active = true };
        list.Add(player);

        membersListView.SelectedItem = player;
        membersListView.ScrollIntoView(player);
      }
    }

    private void MembersViewListContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
      membersListViewAddMenuItem.IsEnabled = (membersListView.ItemsSource != null && membersListView.Items.Count > 0);
      membersListViewLootMenuItem.IsEnabled = (membersListView.SelectedItems.Count > 0 && membersListView.SelectedItems.Count < 20);

      Player player = membersListView.SelectedItem as Player;
      membersListViewClearMenuItem.IsEnabled = (player != null && membersListView.SelectedItems.Count == 1 && DataManager.getFullPlayerList().FirstOrDefault(p => p.Name.Equals(player.Name)) == null);
    }


    private void MembersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      Player player = membersListView.SelectedItem as Player;
      membersListView.CanUserDeleteRows = (player != null && membersListView.SelectedItems.Count == 1 && DataManager.getFullPlayerList().FirstOrDefault(p => p.Name.Equals(player.Name)) == null);
    }

    private void MembersListViewLoot_Click(object sender, RoutedEventArgs e)
    {
      if (membersListView.SelectedItems != null)
      {
        List<string> players = membersListView.SelectedItems.Cast<Player>().Select(player => player.Name).ToList();
        viewLoot(players);
      }
    }

    private void updateMember(string name, Player player)
    {
      string auditLine = DataManager.updateMember(name, player) == 0 ? "S" : "E";
      auditLine += " | " + player.Name + " | " + player.ForumName + " | " + player.Class + " | " + player.Rank + " | " + player.Active;

      if (memberAuditTextBox.Text.Contains("Audit Log"))
      {
        memberAuditTextBox.Clear();
        memberAuditTextBox.AppendText(auditLine);
      }
      else
      {
        memberAuditTextBox.AppendText("\r" + auditLine);
      }
      memberAuditTextBox.ScrollToEnd();
    }

    private void CheckBoxActiveMembers_Checked(object sender, RoutedEventArgs e)
    {
      loadMembers();
    }

    private void LootDetailsListContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
      lootHistoryViewLogMenuItem.IsEnabled = (lootDetailsListView.SelectedItems.Count > 0 && lootDetailsListView.SelectedItems.Count < 20);
    }

    private void GuildChatExpander_Expanded(object sender, RoutedEventArgs e)
    {
      chatGrid.RowDefinitions[1].Height = STAR_GRID;
    }

    private void OfficerChatExpander_Expanded(object sender, RoutedEventArgs e)
    {
      chatGrid.RowDefinitions[2].Height = STAR_GRID;
    }

    private void TellsChatExpander_Expanded(object sender, RoutedEventArgs e)
    {
      chatGrid.RowDefinitions[3].Height = STAR_GRID;
    }

    private void GuildChatExpander_Collapsed(object sender, RoutedEventArgs e)
    {
      chatGrid.RowDefinitions[1].Height = AUTO_GRID;
    }

    private void OfficerChatExpander_Collapsed(object sender, RoutedEventArgs e)
    {
      chatGrid.RowDefinitions[2].Height = AUTO_GRID;
    }

    private void TellsChatExpander_Collapsed(object sender, RoutedEventArgs e)
    {
      chatGrid.RowDefinitions[3].Height = AUTO_GRID;
    }
  }

  public class PercentConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value.GetType() == typeof(int))
      {
        return value.ToString() + "%";
      }
      return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is string)
      {
        int intValue;
        if (!int.TryParse((string) value, out intValue))
        {
          intValue = 0;
        }
        return intValue;
      }
      return 0;
    }
  }

  public class PercentStyleConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      string color = Colors.Black.ToString();

      if (value != null)
      {
        string attendance = value as string;
        if (attendance != null && attendance.Length > 1)
        {
          int intValue = -1;
          int.TryParse(attendance.Substring(0, attendance.Length - 1), out intValue);
          if (intValue > -1)
          {
            if (intValue <= 69)
            {
              color = Colors.DarkRed.ToString();
            }
            else if (intValue <= 79)
            {
              color = "#e46b00";
            }
            else
            {
              color = Colors.DarkGreen.ToString();
            }
          }
        }
      }

      return color.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
