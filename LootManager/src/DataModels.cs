

namespace LootManager
{
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
    public int Main { get; set; }
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
    public string Class { get; set; }
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

  public class LootAuditRecord
  {
    public string Item { get; set; }
    public string Slot { get; set; }
    public string Event { get; set; }
    public bool Alt { get; set; }
    public bool Rot { get; set; }
    public string Date { get; set; }
    public System.DateTime DateValue { get; set; }
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
