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
    private IDictionary<string, ObservableCollection<RequestListItem>> requestListMap = new Dictionary<string, ObservableCollection<RequestListItem>>();
    private ObservableCollection<WatchListItem> watchListItems = new ObservableCollection<WatchListItem>();

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
        DataManager.load();

        Dispatcher.BeginInvoke((System.Action)(() =>
        {
          connectMenuItem.IsEnabled = false;
          disconnectMenuItem.IsEnabled = true;
          Title = Title.Replace("Connecting...", "Connected");
          resetNewLoot(true, true);
        }));
      }).Start();
    }

    private void MenuItemDisconnect_Click(object sender, RoutedEventArgs e)
    {
      TokenManager.cleanup();
      DataManager.cleanup();
      connectMenuItem.IsEnabled = true;
      disconnectMenuItem.IsEnabled = false;
      Title = Title.Replace("Connecting...", "Not Connected");
      Title = Title.Replace("Connected", "Not Connected");
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

    //
    // Looted Item Table Actions
    //
    private void LootedListContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
      lootedListDeleteMenuItem.IsEnabled = (lootedListView.SelectedIndex > -1);
    }

    private void LootedListDelete_Click(object sender, RoutedEventArgs e)
    {
      lootedListView.SelectedItems.Cast<LootedListItem>().ToList().ForEach(item => lootedListItems.Remove(item));
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
      requestListDeleteMenuItem.IsEnabled = (requestListView.SelectedIndex > -1);
    }

    private void RequestListDelete_Click(object sender, RoutedEventArgs e)
    {
      requestListView.SelectedItems.Cast<RequestListItem>().ToList()
        .Where(selected => requestListMap.ContainsKey(selected.Item)).ToList()
        .ForEach(selected => requestListMap[selected.Item].Remove(selected));
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
      watchListDeleteMenuItem.IsEnabled = watchListRenameMenuItem.IsEnabled = (watchListView.SelectedIndex > -1);
    }

    private void WatchListDelete_Click(object sender, RoutedEventArgs e)
    {
      if (watchListView.SelectedIndex >= 0)
      {
        WatchListItem selected = watchListView.SelectedItem as WatchListItem;
        if (selected != null)
        {
          watchListItems.Remove(selected);
          requestListView.ItemsSource = null;
        }
      }
    }

    private void WatchListRename_Click(object sender, RoutedEventArgs e)
    {
      if (watchListView.SelectedIndex >= 0)
      {
        watchListView.CurrentCell = new DataGridCellInfo(watchListView.SelectedItem, watchListView.Columns[0]);
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
        textUpdateTask = Task.Delay(System.TimeSpan.FromMilliseconds(250)).ContinueWith(task =>
        {
          Dispatcher.BeginInvoke((System.Action)(() =>
          {
            bool enable = newLootSlot.SelectedIndex >= 0 && newLootEvent.Text.Length > 0 && !newLootEvent.Text.Equals("Select Event") &&
            newLootItem.Text.Length > 0 && !newLootItem.Text.Equals("Select Item") && newLootPlayer.Text.Length > 0 && !newLootPlayer.Text.Equals("Select Player");
            newLootSaveButton.IsEnabled = enable;
          }));
        });
      }
    }

    private void NewLootItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (newLootItem.SelectedIndex >= 0)
      {
        Item item = newLootItem.SelectedItem as Item;
        if (item != null)
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
      string alt = newLootAlt.IsChecked.Value ? "Yes" : rot;

      if (player.Length > 0 && item.Length > 0 && slot.Length > 0 && eventName.Length > 0 && date.Length > 0 &&
        !player.Equals("Select Player") && !slot.Equals("Any Slot") && !eventName.Equals("Select Event"))
      {
        string auditLine = player + " | " + item + " | " + slot + " | " + eventName + " | " + date;

        try
        {
          IList<object> record = new List<object>() { date, player, eventName, item, slot, rot, alt };
          DataManager.saveLoot(record);

          auditLine = "S " + auditLine;

          LootedListItem listItem = lootedListView.SelectedItem as LootedListItem;
          if (listItem != null && listItem.Player.Equals(player) && listItem.Item.Equals(item))
          {
            lootedListItems.Remove(listItem);
          }

          resetNewLoot(true, false);
        }
        catch(System.Exception ex)
        {
          LOG.Error("Could not save Loot", ex);
          auditLine = "E! " + auditLine;
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

        newLootAuditTextBox.ScrollToEnd();
      }
    }

    private void LootedList_LoadingRow(object sender, DataGridRowEventArgs e)
    {
      LootedListItem theItem = e.Row.Item as LootedListItem;
      if (theItem != null && theItem.Ready)
      {
        e.Row.Background = new SolidColorBrush(Colors.LightGreen);
      }
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
}
