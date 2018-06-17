using System;
using log4net;
using System.Linq;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.Collections.Generic;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using System.Collections.ObjectModel;

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

    // keep track of original row in spreadsheet
    private static string SHEET_ROW = "SheetRow";

    // full datasets
    private static List<Dictionary<string, string>> rosterList = new List<Dictionary<string, string>>();
    private static List<Dictionary<string, string>> lootedList = new List<Dictionary<string, string>>();

    private static Dictionary<string, DateTime?> fileModTimes = new Dictionary<string, DateTime?>();
    private static Dictionary<string, IList<object>> headerMap = new Dictionary<string, IList<object>>();

    // player loot counts
    private static Dictionary<string, LootCounts> lootCountsByName = new Dictionary<string, LootCounts>();
    // sorted player list
    private static List<Player> fullPlayerList = new List<Player>();
    // active player Map
    private static Dictionary<string, Player> activePlayerByName = new Dictionary<string, Player>();
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
    // class type map
    private static readonly Dictionary<string, List<string>> classTypeMap = new Dictionary<string, List<string>>
    {
      { "Tanks", new List<string> { "Paladin", "Shadow Knight", "Warrior" } },
      { "Priests", new List<string> { "Cleric", "Druid", "Shaman" } },
      { "Melee", new List<string> { "Monk", "Rogue", "Berserker" } },
      { "Hybrid", new List<string> { "Bard", "Beastlord", "Ranger" } },
      { "Casters", new List<string> { "Enchanter", "Magician", "Necromancer", "Wizard" } }
    };
    // list of classes
    private static readonly List<string> classTypes = classTypeMap.Keys.ToList();

    // for spreadsheet ranges
    private static readonly char[] rangeCountToLetter = { 'Z', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M' };

    // 1 day in seconds
    private static readonly long D1 = 24 * 60 * 60;

    private static string historyStatus = "";

    public static void load()
    {
      System.DateTime start = System.DateTime.Now;

      // sort class types
      classTypes.Sort();

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

        if ("Yes".Equals(evt[DISPLAY_IN_LIST], StringComparison.OrdinalIgnoreCase))
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
          result = ((start - eventDate).TotalSeconds < 90 * D1);
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

      // get players
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

    public static ObservableCollection<Player> getFullPlayerList()
    {
      return new ObservableCollection<Player>(fullPlayerList);
    }

    public static ObservableCollection<Player> getActivePlayerList()
    {
      return new ObservableCollection<Player>(fullPlayerList.Where(player => player.Active));
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

    public static List<LootDetailsListItem> getLootDetails(List<string> names, List<string> tiers, List<string> classTypes, long? days)
    {
      lootDetailsList.Clear();
      populateLootDetails(names, tiers, classTypes, D1 * (days == null ? 90 : Convert.ToInt64(days)));
      return lootDetailsList.ToList();  // avoids some odd refresh problems
    }

    public static string getHistoryStatus()
    {
      return historyStatus;
    }

    public static List<string> getClassTypes()
    {
      return classTypes.ToList();
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

    public static List<LootAuditRecord> getLootAudit(string player, List<string> tiers, long? days)
    {
      long time = D1 * (days == null ? 90 : Convert.ToInt64(days));
      List<LootAuditRecord> audits = new List<LootAuditRecord>();
      System.DateTime start = System.DateTime.Now;

      foreach (Dictionary<string, string> row in lootedList)
      {
        try
        {
          string name;
          if (row.ContainsKey(NAME) && ((name = row[NAME]) != null) && name.Equals(player, StringComparison.OrdinalIgnoreCase))
          {
            string item = row[ITEM];
            string slot = row[SLOT];
            string eventName = row[EVENT];
            string date = row[DATE];
            System.DateTime dateValue = System.DateTime.Parse(row[DATE]);
            bool alt = row.ContainsKey(ALT_LOOT) && "Yes".Equals(row[ALT_LOOT], StringComparison.OrdinalIgnoreCase);
            bool rot = row.ContainsKey(ROT) && "Yes".Equals(row[ROT], StringComparison.OrdinalIgnoreCase);

            // outside timerange
            if ((start - dateValue).TotalSeconds > time)
            {
              continue;
            }

            if (tiers != null && eventToTier.ContainsKey(eventName) && !tiers.Contains(eventToTier[eventName]))
            {
              continue;
            }

            audits.Add(new LootAuditRecord { Item = item, Slot = slot, Event = eventName, Date = date, DateValue = dateValue, Alt = alt, Rot = rot });
          }
        }
#pragma warning disable CS0168 // Variable is declared but never used
        catch (Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
        {
          LOG.Error("Found bad data in Loot spreadsheet for " + player);
        }
      }

      return audits;
    }

    public static int updateMember(string name, Player player)
    {
      return updateRosterSpreadsheet(name, player);
    }

    public static void cleanup()
    {
      lootCountsByName.Clear();
      fullPlayerList.Clear();
      armorTypesList.Clear();
      eventsList.Clear();
      rosterList.Clear();
      itemsList.Clear();
    }

    private static void populatePlayerList()
    {
      fullPlayerList.Clear();
      rosterList.Clear();

      getModifiedTime(ROSTER_ID);

      // full roster data and sorted players
      readData(rosterList, ROSTER_ID, "Sheet1");

      foreach (Dictionary<string, string> row in rosterList)
      {
        bool active = row.ContainsKey(ACTIVE) && "Yes".Equals(row[ACTIVE], StringComparison.OrdinalIgnoreCase);
        string forumName = row.ContainsKey(FORUM_USERNAME) ? row[FORUM_USERNAME] : "";
        Player player = new Player { Name = row[NAME], Class = row[CLASS], Rank = row[RANK], ForumName = forumName, Active = active };

        if (active && !activePlayerByName.ContainsKey(row[NAME]))
        {
          activePlayerByName.Add(row[NAME], player);
        }

        fullPlayerList.Add(player);
      }

      fullPlayerList.Sort((x, y) => x.Name.CompareTo(y.Name));
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

          if (row.ContainsKey(ALT_LOOT) && "Yes".Equals(row[ALT_LOOT], StringComparison.OrdinalIgnoreCase))
          {
            counts.Alt++;
          }
          else if (!row.ContainsKey(ROT) || !"Yes".Equals(row[ROT], StringComparison.OrdinalIgnoreCase))
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

    private static void populateLootDetails(List<string> names, List<string> tiers, List<string> classTypes, long time)
    {
      int count = 0;
      string oldestDate = null;
      System.DateTime oldestDateValue = System.DateTime.Now;
      System.DateTime start = System.DateTime.Now;

      Dictionary<string, bool> classMap = new Dictionary<string, bool>();
      if (classTypes != null)
      {
        classTypes.ForEach(type => classTypeMap[type].ForEach(c => classMap.Add(c, true)));
      }

      Dictionary<string, LootDetailsListItem> cache = new Dictionary<string, LootDetailsListItem>();

      foreach (Dictionary<string, string> row in lootedList)
      {
        LootDetailsListItem lootDetails;
        System.DateTime theDate;
        string name;

        if (row.ContainsKey(NAME) && ((name = row[NAME]) != null) && row.ContainsKey(DATE) && (theDate = System.DateTime.Parse(row[DATE])) != null)
        {
          // check active player map in addition to name filter
          if (!activePlayerByName.ContainsKey(name) || (names != null && !names.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))))
          {
            continue;
          }

          // outside timerange
          if ((start - theDate).TotalSeconds > time)
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

          string className = activePlayerByName[name].Class;
          if (!classMap.ContainsKey(className))
          {
            continue;
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
              Class = className,
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
          if (row.ContainsKey(ALT_LOOT) && "Yes".Equals(row[ALT_LOOT], StringComparison.OrdinalIgnoreCase))
          {
            isAlt = true;
            lootDetails.Alt++;
          }

          bool isRot = false;
          if (row.ContainsKey(ROT) && "Yes".Equals(row[ROT], StringComparison.OrdinalIgnoreCase))
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

    private static DateTime? getModifiedTime(string docId)
    {
      DriveService driveSvc = new DriveService();
      FilesResource.GetRequest request = driveSvc.Files.Get(docId);
      request.Fields = "modifiedTime";
      request.OauthToken = TokenManager.getAccessToken();
      File file = request.Execute();

      if (fileModTimes.ContainsKey(docId))
      {
        fileModTimes.Remove(docId);
      }

      fileModTimes.Add(docId, file.ModifiedTime);
      return file.ModifiedTime;
    }

    private static void readData(List<Dictionary<string, string>> results, string sheetId, string sheetName)
    {
      ValueRange response = readSpreadsheet(sheetId, sheetName);
      IList<object> headers = response.Values.Take(1).ElementAt(0);

      string headerKey = sheetId + sheetName;
      if (headerMap.ContainsKey(headerKey))
      {
        headerMap.Remove(headerKey);
      }

      // save for future updates
      headerMap.Add(headerKey, headers);

      int sheetRow = 2;
      // Iterate through each row
      foreach (List<object> row in response.Values.Skip(1))
      {
        Dictionary<string, string> record = new Dictionary<string, string>();
        for (int i = 0; i < headers.Count; i++)
        {
          string cellValue = readCell(row, i);
          if (cellValue != null)
          {
            string header = headers[i].ToString().ToLower();
            record.Add(header, cellValue);
          }
        }

        record.Add(SHEET_ROW, sheetRow.ToString());
        results.Add(record);
        sheetRow++;
      }
    }

    private static int updateRosterSpreadsheet(string name, Player player)
    {
      // default to error
      int ret = 1;

      if (fileModTimes.ContainsKey(ROSTER_ID))
      {
        DateTime? previous = fileModTimes[ROSTER_ID];
        DateTime? update = getModifiedTime(ROSTER_ID);
        if (previous != null && update != null && (update - previous).Value.TotalSeconds > 0)
        {
          // file has been updated so re-populate
          populatePlayerList();
        }
      }

      bool newEntry = false;
      Dictionary<string, string> found = rosterList.FirstOrDefault(row => row.ContainsKey(NAME) && row[NAME].Equals(name));
      if (found != null)
      {
        int index = fullPlayerList.FindIndex(p => p.Name.Equals(player.Name));
        if (index >= 0)
        {
          fullPlayerList[index] = player;
        }
      }
      else
      {
        found = new Dictionary<string, string>();
        newEntry = true;
      }

      updateValue(found, ACTIVE, player.Active ? "Yes" : "No");
      updateValue(found, NAME, player.Name);
      updateValue(found, CLASS, player.Class);
      updateValue(found, RANK, player.Rank);
      updateValue(found, FORUM_USERNAME, player.ForumName);

      string headerKey = ROSTER_ID + "Sheet1";
      if (headerMap.ContainsKey(headerKey))
      {
        IList<string> updatedValues = headerMap[headerKey].Select(header =>
        {
          string value = "";
          string key = header.ToString().ToLower();
          if (found.ContainsKey(key))
          {
            value = found[key];
          }

          return value;
        }).ToList();

        DateTime newTime = DateTime.Now;
        if (newEntry)
        {
          appendSpreadsheet(ROSTER_ID, "Sheet1", updatedValues.Cast<object>().ToList());

          // assume roster list has been refresh
          // account for header and starting row index of 1
          found.Add(SHEET_ROW, (rosterList.Count + 2).ToString());
          rosterList.Add(found);
          fullPlayerList.Add(player);
          fullPlayerList.Sort((x, y) => x.Name.CompareTo(y.Name));
        }
        else
        {
          string range = "A" + found[SHEET_ROW] + ":" + rangeCountToLetter[updatedValues.Count] + found[SHEET_ROW];
          updateSpreadsheet(ROSTER_ID, "Sheet1", range, updatedValues.Cast<object>().ToList());
        }

        fileModTimes[ROSTER_ID] = newTime;
        ret = 0;
      }

      return ret;
    }

    private static void updateSpreadsheet(string docId, string sheetName, string range, IList<object> values)
    {
      ValueRange valueRange = new ValueRange();
      valueRange.MajorDimension = "ROWS";
      valueRange.Values = new List<IList<object>> { values };
      valueRange.Range = sheetName + "!" + range;

      SheetsService sheetsSvc = new SheetsService();
      SpreadsheetsResource.ValuesResource.UpdateRequest request =
        sheetsSvc.Spreadsheets.Values.Update(valueRange, docId, sheetName + "!" + range);

      request.OauthToken = TokenManager.getAccessToken();
      request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
      request.Execute();
    }

    private static AppendValuesResponse appendSpreadsheet(string docId, string sheetName, IList<object> values)
    {
      ValueRange valueRange = new ValueRange();
      valueRange.MajorDimension = "ROWS";
      valueRange.Values = new List<IList<object>> { values };

      SheetsService sheetsSvc = new SheetsService();
      SpreadsheetsResource.ValuesResource.AppendRequest request =
        sheetsSvc.Spreadsheets.Values.Append(valueRange, docId, sheetName);

      request.OauthToken = TokenManager.getAccessToken();
      request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
      AppendValuesResponse response = request.Execute();
      return response;
    }

    private static ValueRange readSpreadsheet(string docId, string sheetName)
    {
      SheetsService sheetsSvc = new SheetsService();
      SpreadsheetsResource.ValuesResource.GetRequest request =
              sheetsSvc.Spreadsheets.Values.Get(docId, sheetName);
      request.OauthToken = TokenManager.getAccessToken();
      return request.Execute();
    }

    private static string readCell(List<object> row, int cell)
    {
      string result = null;
      if (row.Count > cell)
      {
        result = row[cell].ToString().Trim();
      }

      return result;
    }

    private static void updateValue(Dictionary<string, string> data, string key, string value)
    {
      if (data.ContainsKey(key))
      {
        data[key] = value;
      }
      else
      {
        data.Add(key, value);
      }
    }
  }
}
