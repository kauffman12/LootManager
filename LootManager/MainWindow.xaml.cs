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
      setLogFile(RuntimeProperties.getProperty("log_file"));
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
      Title = Title.Replace("Reloading Database...", "Connected");
    }

    private void MenuItemSelectEQLogFile_Click(object sender, RoutedEventArgs e)
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
        setLogFile(dlg.FileName);
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
        string result = string.Join(" ", players);
        if (result != null && result.Length > 3)
        {
          lootHistoryTab.Focus();
          lootDetailsFilterBox.FontStyle = FontStyles.Normal;
          lootDetailsFilterBox.Text = result;
        }
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
    private void setLogFile(string path)
    {
      if (path != null)
      {
        LogReader.getInstance().setLogFile(path);
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
            //[Sun May 13 20:20:29 2018] --(You) have (looted) a (Enchanted Runestone).--
            if (e.matches[0].Groups.Count == 4)
            {
              string item = e.matches[0].Groups[3].Value;
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

        List<string> process = new List<string>();
        bool hasMain = false;
        foreach (RequestListItem requestItem in requestList)
        {
          string displayName = requestItem.Player;
          if (!"Main".Equals(requestItem.Type))
          {
            displayName += "(" + requestItem.Type + ")";
          }
          else
          {
            hasMain = true;
          }

          process.Add(displayName);
        }

        foreach (string displayName in process)
        {
          // if show all is not checked then only display alts and rot if no mains are available
          if (lootAllTypes.IsChecked.Value || !hasMain || !displayName.Contains("("))
          {
            chat += " - " + displayName;
          }
        }
      }

      if ("".Equals(chat) && genChatBox.FontStyle != FontStyles.Italic)
      {
        resetGenChatBox();
      }
      else
      {
        genChatBox.FontStyle = FontStyles.Normal;
        genChatBox.Text = chat;
      }
    }

    private void resetGenChatBox()
    {
      genChatBox.FontStyle = FontStyles.Italic;
      genChatBox.Text = "Suggested Officer Chat for Selected Item";
    }

    private void LootedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (lootedListView.SelectedIndex >= 0)
      {
        LootedListItem listItem = lootedListView.SelectedItem as LootedListItem;
        List<Player> players = newLootPlayer.ItemsSource as List<Player>;
        if (players != null)
        {
          newLootPlayer.SelectedIndex = players.FindIndex(player => player.Name.Equals(listItem.Player));
        }

        if (newLootPlayer.SelectedIndex == -1)
        {
          newLootPlayer.Text = listItem.Player;
        }

        List<Item> found = DataManager.getItemsList().Where(item => item.Name.Equals(listItem.Item)).ToList();
        if (found.Count > 0)
        {
          newLootItem.ItemsSource = found;
          newLootItem.SelectedIndex = 0;
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
            newLootItem.Text.Length > 0 && !newLootItem.Text.Equals("Select Item") && newLootPlayer.Text.Length > 0 && !newLootPlayer.Text.Equals("Select Player"));

            updateItemsDB.IsEnabled = (newLootItem.Text.Length > 0 && !newLootItem.Text.Equals("Select Item") && newLootItem.SelectedIndex == -1);
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
          if (index >= 0)
          {
            newLootSlot.SelectedIndex = index;
          }
        }

        List<RaidEvent> events = newLootEvent.ItemsSource as List<RaidEvent>;
        if (!"Any".Equals(item.EventName))
        {
          newLootEvent.SelectedIndex = events.FindIndex(evt => evt.ShortName.Equals(item.EventName));
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
      if (newLootEvent == null)
      {
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

      if (force || (newLootEvent.SelectedIndex == -1 && newLootEvent.Text.Trim().Length == 0))
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
        Clipboard.SetText(genChatBox.Text);
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
          tierComboBox.SelectedItemsOverride = DataManager.getTiers();
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
        lootDetailsListView.ItemsSource = DataManager.getLootDetails(names, tiers, timeSpinner.Value);
        historyStatusText.Content = DataManager.getHistoryStatus();
      }
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

  public class LootedListItem
  {
    public string Item { get; set; }
    public string Player { get; set; }
    public string Slot { get; set; }
    public string Found { get; set; }
    public bool Ready { get; set; }
  }

  public class RequestListItem
  {
    public string Item { get; set; }
    public string Player { get; set; }
    public string Type { get; set; }
    public int Main{ get; set; }
    public int Alt { get; set; }
    public int Days { get; set; }
  }

  public class WatchListItem
  {
    public string Item { get; set; }
    public int TellCount { get; set; }
    public string Found { get; set; }
  }

  public class LootDetailsListItem
  {
    public string Player { get; set; }
    public int Total { get; set; }
    public int Visibles { get; set; }
    public int NonVisibles { get; set; }
    public int Weapons { get; set; }
    public int Other { get; set; }
    public int Special { get; set; }
    public int Main { get; set; }
    public int Alt { get; set; }
    public int Rot { get; set; }
    public string LastAltDate { get; set; }
    public string LastMainDate { get; set; }
    public System.DateTime LastAltDateValue { get; set; }
    public System.DateTime LastMainDateValue { get; set; }
  }
}
