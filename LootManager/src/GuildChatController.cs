﻿using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Windows.Documents;

namespace LootManager
{
  public class GuildChatController : SimpleChatController
  {
    private Regex aNumber = new Regex(@"\d");
    private Regex otherLootChat = new Regex(@"^\w+ tells the guild, '/t (.+)'");
    private Regex yourLootChat = new Regex(@"^You say to your guild, '/t (.+)'");
    private DataGrid watchListView;
    private CheckBox lootChatOnly;
    private List<string> guildChatBuffer = new List<string>();

    public GuildChatController(RichTextBox richTextBox, DataGrid watchListView, CheckBox lootChatOnly, CheckBox autoScroll) : base(richTextBox, autoScroll)
    {
      this.watchListView = watchListView;
      this.lootChatOnly = lootChatOnly;
    }

    public new void handleEvent(LogEventArgs e)
    {
      string line = parseLine(e.line);
      MatchCollection matches = yourLootChat.Matches(line);

      if (matches.Count > 0 && matches[0].Groups.Count == 2)
      {
        string lootString = matches[0].Groups[1].Value;
        if (lootString.Length > 0 && char.IsUpper(lootString[0]))
        {
          // find loot in database
          string item = "";
          WatchListItem listItem = null;

          // try to cut out end with possibly digit like x2
          string output = System.String.Join(" ", lootString.Split(null).Where(x => !aNumber.IsMatch(x)));
      
          foreach (string piece in output.Split(null))
          {
            if (!aNumber.IsMatch(piece))
            {
              item = (item.Length == 0) ? piece : item + " " + piece;

              object foundItem = DataManager.findItem(item);
              if (foundItem != null)
              {
                if (foundItem.GetType() == typeof(Item))
                {
                  listItem = new WatchListItem { Item = (foundItem as Item).Name, Found = "Yes" };
                  break;
                }
                else if ((int) foundItem == -1)
                {
                  // not even a partial match
                  break;
                }
              }
            }
          }

          if (listItem == null)
          {
            // we got here and didn't find the loot so use what we have so far
            listItem = new WatchListItem { Item = output, TellCount = 0, Found = "No, Edit if needed" };
          }

          ObservableCollection<WatchListItem> collection = watchListView.ItemsSource as ObservableCollection<WatchListItem>;
          if (collection == null || collection.Count <= 0 || collection.FirstOrDefault(x => listItem.Item.Equals(x.Item)) == null)
          {
            collection.Add(listItem);
          }
        }
      }

      bool isLootChat = matches.Count > 0 || otherLootChat.IsMatch(line) || (e.line.Split(new string[] { " // " }, System.StringSplitOptions.None).Length > 1) ||
        (e.line.Split(new string[] { " || " }, System.StringSplitOptions.None).Length > 1);

      if (!isLootChat)
      {
        if (lootChatOnly.IsChecked.Value)
        {
          guildChatBuffer.Add(line);
        }
        else
        {
          appendLine(line);
        }
      }
      else
      {
        guildChatBuffer.Add(line);
        appendLine(line);
      }
    }

    public void toggleDisplayLootOnly()
    {
      string[] temp = { };
      TextRange range = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd);
      if (range.Text.Length > 0)
      {
        temp = range.Text.TrimEnd().Split('\r');
      }

      range.Text = (guildChatBuffer.Count > 0) ? System.String.Join("\r", guildChatBuffer.ToArray()) : "";

      if (temp.Length > 0)
      {
        guildChatBuffer = new List<string>(temp);
      }
      else
      {
        guildChatBuffer = new List<string>();
      }

      if (autoScroll.IsChecked.Value)
      {
        richTextBox.ScrollToEnd();
      }
    }
  }
}