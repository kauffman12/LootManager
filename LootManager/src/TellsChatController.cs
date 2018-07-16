using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections;
using System.Windows.Documents;
using System.Globalization;

namespace LootManager
{
  public class TellsChatController : SimpleChatController
  {
    private Regex aNumber = new Regex(@"\d");
    private Regex lootTell = new Regex(@"^(\D+) tells you, '(.+)'");
    private Regex timeStamp = new Regex(@"^\[(\w{3} \w{3} \d+) (\d{2}:\d{2}).*\].+");
    private DataGrid watchListView;
    private IDictionary<string, ObservableCollection<RequestListItem>> requestListMap;
    private CheckBox lootChatOnly;
    private FlowDocument tellsChatBuffer;

    public TellsChatController(RichTextBox richTextBox, DataGrid watchListView, IDictionary<string, ObservableCollection<RequestListItem>> requestListMap, 
      CheckBox lootChatOnly, CheckBox autoScroll) : base(richTextBox, autoScroll)
    {
      this.watchListView = watchListView;
      this.requestListMap = requestListMap;
      this.lootChatOnly = lootChatOnly;

      tellsChatBuffer = new FlowDocument();
      tellsChatBuffer.Blocks.Add(new Paragraph());
      tellsChatBuffer.Blocks.Add(new Paragraph());
    }

    public new void Clear()
    {
      base.Clear();
      tellsChatBuffer.Blocks.Clear();
    }

    public new void handleEvent(LogEventArgs e)
    {
      string line = parseLine(e.line);
      bool wasLoot = false;
      string highlight = null;

      MatchCollection matches = lootTell.Matches(line);
      if (matches.Count > 0)
      {
        string name = matches[0].Groups[1].Value;
        string text = matches[0].Groups[2].Value;

        // read time
        string time = "";
        System.DateTime added = System.DateTime.Now;
        MatchCollection timeMatch = timeStamp.Matches(e.line);
        if (timeMatch.Count == 1 && timeMatch[0].Groups.Count > 2)
        {
          time = timeMatch[0].Groups[2].Value;
          added = System.DateTime.ParseExact(timeMatch[0].Groups[1].Value + " " + time, "ddd MMM dd HH:mm", CultureInfo.InvariantCulture);
        }

        ObservableCollection<WatchListItem> watchList = watchListView.ItemsSource as ObservableCollection<WatchListItem>;
        if (watchList != null)
        {
          WatchListItem found = watchList.FirstOrDefault(x => text.Contains(x.Item));
          if (found != null)
          {
            highlight = found.Item;
            string cleaned = text.Replace(found.Item, "").ToLower();
            string type = "Main";

            foreach (string test in cleaned.Split(null))
            {
              if (test.Equals("alt"))
              {
                type = "Alt";
                wasLoot = true;
                break;
              }
              else if (test.Equals("rot") || test.Equals("rots") || test.Equals("rotting") || test.Equals("roting"))
              {
                type = "Rot";
                wasLoot = true;
                break;
              }
              else if (test.Equals("main"))
              {
                // default type
                wasLoot = true;
                break;
              }
            }

            // check that a list of players exists for the given Item
            if (requestListMap.ContainsKey(found.Item))
            {
              wasLoot = true;
              ObservableCollection<RequestListItem> list = requestListMap[found.Item];

              // case of updating existing
              RequestListItem foundRequestListItem = list.FirstOrDefault(x => name.Equals(x.Player));
              if (foundRequestListItem != null && (added - foundRequestListItem.Added).TotalSeconds > 600)
              {
                list.Remove(foundRequestListItem);
              }

              // if nothing selected then select first item we received tells for
              // also do this early so the requestListItem change event sees it selected
              if (watchListView.SelectedIndex == -1)
              {
                watchListView.SelectedItem = found;
              }

              // Add player
              RequestListItem requestListItem = new RequestListItem {
                Item = found.Item,
                Player = name,
                Type = type,
                Main = 0,
                Alt = 0,
                Days = -1,
                Time = time,
                Added = added
              };

              // Update with loot counts
              DataManager.updateLootCounts(requestListItem);
              list.Add(requestListItem);

              // update count
              found.TellCount = list.Count;
              watchListView.Items.Refresh();

              IList selectList = new ArrayList();
              selectList.Add(watchListView.SelectedItem);
              watchListView.RaiseEvent(new SelectionChangedEventArgs(DataGrid.SelectionChangedEvent, selectList, selectList));
            }
          }
        }
      }

      if (!wasLoot)
      {
        if (lootChatOnly.IsChecked.Value)
        {
          appendLine(tellsChatBuffer, line, highlight);
        }
        else
        {
          appendLine(richTextBox.Document, line, highlight);
        }
      }
      else
      {
        appendLine(tellsChatBuffer, line, highlight);
        appendLine(richTextBox.Document, line, highlight);
      }
    }

    public void toggleDisplayLootOnly()
    {
      FlowDocument temp = richTextBox.Document;
      richTextBox.Document = tellsChatBuffer;
      tellsChatBuffer = temp;

      if (autoScroll.IsChecked.Value)
      {
        richTextBox.ScrollToEnd();
      }
    }
  }
}