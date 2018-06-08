using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace LootManager
{
  public static class RuntimeProperties
  {
    private static string FILE_NAME = "properties.json";
    private static IDictionary<string, string> properties = new Dictionary<string, string>();

    public static void setProperty(string name, string value)
    {
      if (properties.ContainsKey(name))
      {
        properties.Remove(name);
      }

      properties.Add(name, value);
    }

    public static string getProperty(string name)
    {
      string result = null;
      if (properties.ContainsKey(name))
      {
        result = properties[name];
      }

      return result;
    }

    public static void serialize()
    {
      string json = JsonConvert.SerializeObject(properties, Formatting.Indented);
      File.WriteAllText(System.AppDomain.CurrentDomain.BaseDirectory + "\\" + FILE_NAME, json);
    }

    public static void deserialize()
    {
      string fileName = System.AppDomain.CurrentDomain.BaseDirectory + "\\" + FILE_NAME;
      if (File.Exists(fileName))
      {
        string json = File.ReadAllText(fileName);
        properties = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
      }
    }
  }
}