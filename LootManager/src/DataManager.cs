using System;
using log4net;
using System.Linq;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.Collections.Generic;

namespace LootManager
{
  static class DataManager
  {
    private static readonly ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    // document IDs
    private static string ROSTER_ID = "1J3Io-COBeCaAQ_jTiJS9kmdP8gqpFNr2_5-gfY_c5cg";
    private static string LOOT_ID = "1fGMG78HVN8iLO43zoi8Ermt_nhfkqBHcQkdQLQ7BqnI";
    private static string ITEMS_ID = "13_qG0syQGgK7-yT06r5MJ3HNrWmVh0872ou-FhXjjDM";

    // ROI Loot.xlsx
    // -- RainOfFearLoot      Date, Name, Event, Item, Slot, Rot, Alt Loot
    // -- RainOfFearRaids     Event, Short Name, Tier, Display In List
    // -- Constants           Armor Types, Tiers
    // ROI Items Sheet.xlsx
    // -- ROFItems            Slot, Item Name, Event, Special
    // -- ROF Global Drops    Slot, Item Name, Tier, Special
    // Roster.xlsx
    // -- Sheet1              Name, Class, Rank, Active, Forum Username, Global Loot Viewer
    private static string ACTIVE = "Active".ToLower();
    private static string ALT_LOOT = "Alt Loot".ToLower();
    private static string ARMOR_TYPES = "Armor Types".ToLower();
    private static string CLASS = "Class".ToLower();
    private static string DATE = "Date".ToLower();
    private static string DISPLAY_IN_LIST = "Display In List".ToLower();
    private static string EVENT = "Event".ToLower();
    private static string FORUM_USERNAME = "Forum Username".ToLower();
    private static string GLOBAL_LOOT_VIEWER = "Global Loot Viewer".ToLower();
    private static string ITEM = "Item".ToLower();
    private static string ITEM_NAME = "Item Name".ToLower();
    private static string RANK = "Rank".ToLower();
    private static string ROT = "Rot".ToLower();
    private static string NAME = "Name".ToLower();
    private static string SHORT_NAME = "Short Name".ToLower();
    private static string SLOT = "Slot".ToLower();
    private static string SPECIAL = "Special".ToLower();
    private static string TIER = "Tier".ToLower();

    // full datasets
    private static List<Dictionary<string, string>> rosterList = new List<Dictionary<string, string>>();
    private static List<Dictionary<string, string>> lootedList = new List<Dictionary<string, string>>();

    // player loot counts
    private static Dictionary<string, LootCounts> lootCountsByName = new Dictionary<string, LootCounts>();
    // sorted play list
    private static List<Player> activePlayerList = new List<Player>();
    // sorted events list
    private static List<RaidEvent> eventsList = new List<RaidEvent>();
    // sorted item list
    private static List<Item> itemsList = new List<Item>();
    // sorted armor types list
    private static List<string> armorTypesList = new List<string>();
    // sorted list of loot details
    private static List<LootDetailsListItem> lootDetailsList = new List<LootDetailsListItem>();

    // map of Events to Tiers
    private static Dictionary<string, string> eventToTier = new Dictionary<string, string>();

    // map of visible armor
    private static readonly Dictionary<string, bool> visibleArmorTypes = new Dictionary<string, bool>
    {
      { "Arms", true }, { "Chest", true }, { "Feet", true }, { "Hands", true }, { "Head", true },
      { "Legs", true }, { "Wrist", true }
    };
    // map of non-visible armor
    private static readonly Dictionary<string, bool> nonVisibleArmorTypes = new Dictionary<string, bool>
    {
      { "Back", true }, { "Charm", true }, { "Ear", true }, { "Face", true }, { "Neck", true },
      { "Range", true }, { "Ring", true }, { "Shoulders", true }, { "Shield", true }, { "Waist", true }
    };
    // map of weapon types
    private static readonly Dictionary<string, bool> weaponTypes = new Dictionary<string, bool>
    {
      { "1HB", true }, { "1HP", true }, { "1HS", true }, { "2HB", true }, { "2HP", true },
      { "2HS", true }, { "H2H", true }, { "Bow", true }
    };

    // 1 day in seconds
    private static readonly long D1 = 24 * 60 * 60;

    // timeframes  Any, 30 days, 60 days, 90 days
    // should match UI control order
    private static readonly long[] timeFrames = { D1*9999, D1*30, D1*60, D1*90 };

    private static string historyStatus = "";

    public static void load()
    {
      System.DateTime start = System.DateTime.Now;

      // temp list
      List<Dictionary<string, string>> temp = new List<Dictionary<string, string>>();

      // read raid events and filter out ones that arent active
      readData(temp, LOOT_ID, "RainOfFearRaids");
      temp.ForEach(evt =>
      {
        if (!eventToTier.ContainsKey(evt[SHORT_NAME]))
        {
          eventToTier.Add(evt[SHORT_NAME], evt[TIER]);
        }

        if ("Yes".Equals(evt[DISPLAY_IN_LIST]))
        {
          eventsList.Add(new RaidEvent { Name = evt[EVENT], ShortName = evt[SHORT_NAME], Tier = evt[TIER] });
        }
      });

      eventsList.Sort((x, y) => x.ShortName.CompareTo(y.ShortName));

      // read all loot data and remove everything older than 90 days
      temp.Clear();
      readData(lootedList, LOOT_ID, "RainOfFearLoot");
      temp = lootedList.Where(evt =>
      {
        bool result = false;
        try
        {
          System.DateTime eventDate = System.DateTime.Parse(evt[DATE]);
          result = ((start - eventDate).TotalSeconds < timeFrames[3]);
        }
        catch (Exception e)
        {
          LOG.Error("Error parsing Date on RainOfFearLoot tab: " + evt[DATE], e);
        }

        return result;
      }).ToList();

      // player loot counts
      populateLootCounts(temp, start);

      // sorted items
      temp.Clear();
      readData(temp, ITEMS_ID, "ROFItems");
      populateItemsList(temp);
      temp.Clear();
      readData(temp, ITEMS_ID, "ROF Global Drops");
      populateItemsList(temp);

      // full roster data and sorted players
      readData(rosterList, ROSTER_ID, "Sheet1");
      populatePlayerList();

      // armor types
      temp.Clear();
      readData(temp, LOOT_ID, "Constants");
      temp.Where(d => d.ContainsKey(ARMOR_TYPES)).ToList().ForEach(d => armorTypesList.Add(d[ARMOR_TYPES]));
      armorTypesList.Sort();
      armorTypesList.Insert(0, "Any Slot");

      LOG.Debug("Finished Loading Data in " + (System.DateTime.Now - start).TotalSeconds + " seconds");
    }

    // -1 means no matches found at all
    //  0 means perfect match
    //  1 means partial match, try again
    public static object findItem(string search)
    {
      object result = -1;

      // search list
      List<Item> found = itemsList.Where(item => item.Name.StartsWith(search)).ToList();
      if (found.Count == 1 && found[0].Name.Equals(search))
      {
        result = found[0];
      }
      else
      {
        result = found.Count == 0 ? -1 : 1;
      }

      return result;
    }

    public static void updateLootCounts(RequestListItem requestListItem)
    {
      if (lootCountsByName.ContainsKey(requestListItem.Player))
      {
        LootCounts counts = lootCountsByName[requestListItem.Player];
        requestListItem.Main = counts.Main;
        requestListItem.Alt = counts.Alt;
        requestListItem.Days = counts.LastMainDays;
      }
    }

    public static List<Player> getActivePlayerList()
    {
      return activePlayerList;
    }

    public static List<Item> getItemsList()
    {
      return itemsList;
    }

    public static List<String> getArmorTypes()
    {
      return armorTypesList;
    }

    public static List<RaidEvent> getEventsList()
    {
      return eventsList;
    }

    public static List<LootDetailsListItem> getLootDetails(List<string> names, List<string> tiers, int timeFrame)
    {
      lootDetailsList.Clear();
      populateLootDetails(names, tiers, timeFrame);
      return lootDetailsList.ToList();  // avoids some odd refresh problems
    }

    public static string getHistoryStatus()
    {
      return historyStatus;
    }

    public static List<string> getTiers()
    {
      return eventToTier.Values.Distinct().ToList();
    }

    public static void saveLoot(string date, string player, string eventName, string item, string slot, string rot, string alt)
    {
      appendSpreadsheet(LOOT_ID, "RainOfFearLoot", new List<object>() { date, player, eventName, item, slot, rot, alt });
    }

    public static void saveItem(string slot, string item, string eventName)
    {
      // Assume non-global item for now
      appendSpreadsheet(ITEMS_ID, "ROFItems", new List<object>() { slot, item, eventName });
      itemsList.Add(new Item { Name = item, Slot = slot, EventName = eventName, Tier = "" });

      // resort
      itemsList.Sort((x, y) => x.Name.CompareTo(y.Name));
    }

    public static void cleanup()
    {
      lootCountsByName.Clear();
      activePlayerList.Clear();
      armorTypesList.Clear();
      eventsList.Clear();
      rosterList.Clear();
      itemsList.Clear();
    }

    private static void populatePlayerList()
    {
      foreach (Dictionary<string, string> row in rosterList)
      {
        if (row.ContainsKey(ACTIVE) && row[ACTIVE].ToLower().Equals("yes"))
        {
          activePlayerList.Add(new Player { Name = row[NAME], Class = row[CLASS], Rank = row[RANK] });
        }
      }

      activePlayerList.Sort((x, y) => x.Name.CompareTo(y.Name));
    }

    private static void populateItemsList(List<Dictionary<string, string>> list)
    {
      foreach (Dictionary<string, string> item in list)
      {
        // handle global loot
        string eventName = "Any";
        if (item.ContainsKey(EVENT))
        {
          eventName = item[EVENT];

          // make sure item is associated with current event
          if (!eventsList.Any(evt => evt.ShortName.Equals(eventName)))
          {
            continue;
          }
        }

        string tier = "";
        if (item.ContainsKey(TIER))
        {
          tier = item[TIER];
          if (!eventsList.Any(evt => evt.Tier.Equals(tier)))
          {
            continue;
          }
        }

        itemsList.Add(new Item { Name = item[ITEM_NAME], Slot = item[SLOT], EventName = eventName, Tier = tier });
      }

      itemsList.Sort((x, y) => x.Name.CompareTo(y.Name));
    }

    private static void populateLootCounts(List<Dictionary<string, string>> lootedSubList, DateTime start)
    {
      foreach (Dictionary<string, string> row in lootedSubList)
      {
        LootCounts counts;
        string name;

        if (row.ContainsKey(NAME) && ((name = row[NAME]) != null))
        {
          if (lootCountsByName.ContainsKey(name))
          {
            counts = lootCountsByName[name];
          }
          else
          {
            counts = new LootCounts { Main = 0, Alt = 0, LastMainDays = -1 };
            lootCountsByName.Add(name, counts);
          }

          if (row.ContainsKey(ALT_LOOT) && "Yes".Equals(row[ALT_LOOT]))
          {
            counts.Alt++;
          }
          else if (!row.ContainsKey(ROT) || !"Yes".Equals(row[ROT]))
          {
            counts.Main++;
            int days = Convert.ToInt32((start - System.DateTime.Parse(row[DATE])).TotalDays);
            if (counts.LastMainDays == -1 || counts.LastMainDays > days)
            {
              counts.LastMainDays = days;
            }
          }
        }
      }
    }

    private static void populateLootDetails(List<string> names, List<string> tiers, int timeFrame)
    {
      int count = 0;
      string oldestDate = null;
      System.DateTime oldestDateValue = System.DateTime.Now;
      System.DateTime start = System.DateTime.Now;

      Dictionary<string, LootDetailsListItem> cache = new Dictionary<string, LootDetailsListItem>();

      foreach (Dictionary<string, string> row in lootedList)
      {
        LootDetailsListItem lootDetails;
        System.DateTime theDate;
        string name;

        if (row.ContainsKey(NAME) && ((name = row[NAME]) != null) && row.ContainsKey(DATE) && (theDate = System.DateTime.Parse(row[DATE])) != null)
        {
          if (names != null && !names.Contains(name))
          {
            continue;
          }

          // outside timerange
          if (timeFrames.Length > timeFrame && timeFrame > -1 && (start - theDate).TotalSeconds > timeFrames[timeFrame])
          {
            continue;
          }

         string eventName;
          if (tiers != null && row.ContainsKey(EVENT) && ((eventName = row[EVENT]) != null))
          {
            if (eventToTier.ContainsKey(eventName) && !tiers.Contains(eventToTier[eventName]))
            {
              continue;
            }
          }

          count++;

          if (oldestDate == null || oldestDateValue.CompareTo(theDate) > 0)
          {
            oldestDateValue = theDate;
            oldestDate = row[DATE];
          }

          if (cache.ContainsKey(name))
          {
            lootDetails = cache[name];
          }
          else
          {
            // Not handling special right now
            lootDetails = new LootDetailsListItem
            {
              Player = name,
              Total = 0,
              Visibles = 0,
              NonVisibles = 0,
              Weapons = 0,
              Special = 0,
              Main = 0,
              Alt = 0,
              Rot = 0,
              LastAltDate = "",
              LastMainDate = ""
            };
            cache.Add(name, lootDetails);
          }

          bool isAlt = false;
          if (row.ContainsKey(ALT_LOOT) && "Yes".Equals(row[ALT_LOOT]))
          {
            isAlt = true;
            lootDetails.Alt++;
          }

          bool isRot = false;
          if (row.ContainsKey(ROT) && "Yes".Equals(row[ROT]))
          {
            isRot = true;
            lootDetails.Rot++;
          }

          if (!isRot && !isAlt)
          {
            lootDetails.Main++;
          }

          string slot;
          if (row.ContainsKey(SLOT) && ((slot = row[SLOT]) != null))
          {
            bool added = false;
            if (visibleArmorTypes.ContainsKey(slot))
            {
              lootDetails.Visibles++;
              added = true;
            }
            else if (nonVisibleArmorTypes.ContainsKey(slot))
            {
              lootDetails.NonVisibles++;
              added = true;
            }
            else if (weaponTypes.ContainsKey(slot))
            {
              lootDetails.Weapons++;
              added = true;
            }
            else
            {
              lootDetails.Other++;
            }

            if (added)
            {
              lootDetails.Total++;
            }
          }

          if (isAlt)
          {
            if (lootDetails.LastAltDate.Length == 0 || lootDetails.LastAltDateValue.CompareTo(theDate) < 0)
            {
              lootDetails.LastAltDate = row[DATE];
              lootDetails.LastAltDateValue = theDate;
            }
          }
          else if (!isRot)
          {
            if (lootDetails.LastMainDate.Length == 0 || lootDetails.LastMainDateValue.CompareTo(theDate) < 0)
            {
              lootDetails.LastMainDate = row[DATE];
              lootDetails.LastMainDateValue = theDate;
            }
          }
        }
      }

      cache.Values.Cast<LootDetailsListItem>().ToList().ForEach(item => lootDetailsList.Add(item));
      lootDetailsList.Sort((x, y) => x.Player.CompareTo(y.Player));

      historyStatus = "Found " + count + " Entries Matching Filters, First Entry Recorded On " + oldestDate;
    }

    private static void readData(List<Dictionary<string, string>> results, string sheetId, string sheetName)
    {
      ValueRange response = readSpreadsheet(sheetId, sheetName);
      IList<Object> headers = response.Values.Take(1).ElementAt(0);

      // Iterate through each row
      foreach (List<Object> row in response.Values.Skip(1))
      {
        Dictionary<string, string> record = new Dictionary<string, string>();
        for (int i=0; i<headers.Count; i++)
        {
          string cellValue = readCell(row, i);
          if (cellValue != null)
          {
            string header = headers[i].ToString().ToLower();
            record.Add(header, cellValue);
          }
        }

        results.Add(record);
      }
    }

    private static void appendSpreadsheet(string docId, string sheetName, IList<Object> values)
    {
      ValueRange valueRange = new ValueRange();
      valueRange.MajorDimension = "ROWS";
      valueRange.Values = new List<IList<Object>> { values };

      SheetsService sheetsSvc = new SheetsService();
      SpreadsheetsResource.ValuesResource.AppendRequest request =
        sheetsSvc.Spreadsheets.Values.Append(valueRange, docId, sheetName);

      request.OauthToken = TokenManager.getAccessToken();
      request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
      request.Execute();
    }

    private static ValueRange readSpreadsheet(string docId, string sheetName)
    {
      SheetsService sheetsSvc = new SheetsService();
      SpreadsheetsResource.ValuesResource.GetRequest request =
              sheetsSvc.Spreadsheets.Values.Get(docId, sheetName);
      request.OauthToken = TokenManager.getAccessToken();
      return request.Execute();
    }

    private static string readCell(List<Object> row, int cell)
    {
      string result = null;
      if (row.Count > cell)
      {
        result = row[cell].ToString().Trim();
      }

      return result;
    }
  }

  public class RaidEvent
  {
    public string Name { get; set; }
    public string ShortName { get; set; }
    public string Tier { get; set; }
  }

  public class LootCounts
  {
    public int Main { get; set; }
    public int Alt { get; set; }
    public int LastMainDays { get; set; }
  }

  public class Player
  {
    public string Name { get; set; }
    public string Class { get; set; }
    public string Rank { get; set; }
  }

  public class Item
  {
    public string Name { get; set; }
    public string Slot { get; set; }
    public string EventName { get; set; }
    public string Tier { get; set; }
  }
}
