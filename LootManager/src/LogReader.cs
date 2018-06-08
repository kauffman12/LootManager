using log4net;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace LootManager
{
  public class LogReader
  {
    private static readonly ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
    private static LogReader instance = new LogReader();

    // A delegate type for hooking up change notifications.
    public enum LOG_TYPE { GUILD_CHAT, OFFICER_CHAT, TELLS, LOOT };
    public delegate void LogReaderEvent(object sender, LogEventArgs e);
    public event LogReaderEvent logEvent;

    private string logFilePath;
    private string logFileName;
    private Thread myThread;
    private ThreadState threadState;
    private Regex guildChat = new Regex(@"^\[.+\] \w+ (tells the guild|say to your guild)");
    private Regex officerChat = new Regex(@"^\[.+\] \w+ tell?(\w) (?i)officersofroi(?-i):");
    private Regex tells = new Regex(@"^\[.+\] \w+ tells you, '");
    private Regex lootedItem = new Regex(@"^\[.+\] --(\w+) (has|have) looted a (.+)\.--");
    private Regex userFromFileName = new Regex(@"^eqlog_([a-zA-Z]+)_");

    private LogReader() { }

    public static LogReader getInstance()
    {
      return instance;
    }

    public void setLogFile(string filename)
    {
      LOG.Debug("Selecting EQ Log File: " + filename);

      if (File.Exists(filename))
      {
        logFilePath = filename.Substring(0, filename.LastIndexOf("\\")) + "\\";
        logFileName = filename.Substring(filename.LastIndexOf("\\") + 1);

        MatchCollection matches = userFromFileName.Matches(logFileName);
        if (matches.Count > 0)
        {
          LOG.Debug("Found Player: " + matches[0].Groups[1].Value);
          RuntimeProperties.setProperty("player", matches[0].Groups[1].Value);
        }

        // save log file to settings
        RuntimeProperties.setProperty("log_file", filename);

        start();
      }
      else
      {
        LOG.Error("Selected EQ Log File missing: " + filename);
      }
    }

    public void stop()
    {
      if (threadState != null)
      {
        threadState.stop();
      }

      if (myThread != null)
      {
        myThread.Join(3000);
      }
    }

    private void start()
    {
      if (threadState != null)
      {
        LOG.Debug("Stopping previous LogReader thread");
        threadState.stop();
      }

      threadState = new ThreadState();
      ThreadState myState = threadState;

      myThread = new Thread(() =>
      {
        bool exitOnError = false;

        // get file stream
        FileStream fs = new FileStream(logFilePath + logFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        StreamReader reader = new StreamReader(fs);

        // setup watcher
        FileSystemWatcher fsw = new FileSystemWatcher
        {
          Path = logFilePath,
          Filter = logFileName
        };

        // events to notify for changes
        fsw.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.CreationTime;

        // read to end and start listening for events
        reader.ReadToEnd();
        fsw.EnableRaisingEvents = true;

        while (myState.isRunning() && !exitOnError)
        {
          WaitForChangedResult result = fsw.WaitForChanged(WatcherChangeTypes.Deleted | WatcherChangeTypes.Changed, 2000);

          // check if exit during wait period
          if (!myState.isRunning() || exitOnError)
          {
            break;
          }

          switch (result.ChangeType)
          {
            case WatcherChangeTypes.Deleted:
              // file gone
              LOG.Error("EQ Log File removed!?");
              exitOnError = true;
              break;
            case WatcherChangeTypes.Changed:
              if (reader != null)
              {
                while (!reader.EndOfStream)
                {
                  string line = reader.ReadLine();
                  if (logEvent != null && line.Length > 0)
                  {
                    MatchCollection matches;
                    if ((matches = officerChat.Matches(line)).Count > 0)
                    {
                      logEvent(this, new LogEventArgs(line, matches, LOG_TYPE.OFFICER_CHAT));
                    }
                    else if ((matches = guildChat.Matches(line)).Count > 0)
                    {
                      logEvent(this, new LogEventArgs(line, matches, LOG_TYPE.GUILD_CHAT));
                    }
                    else if ((matches = tells.Matches(line)).Count > 0)
                    {
                      logEvent(this, new LogEventArgs(line, matches, LOG_TYPE.TELLS));
                    }
                    else if ((matches = lootedItem.Matches(line)).Count > 0)
                    {
                      logEvent(this, new LogEventArgs(line, matches, LOG_TYPE.LOOT));
                    }
                  }
                }
              }
              break;
          }
        }

        if (reader != null)
        {
          reader.Close();
        }

        if (fs != null)
        {
          fs.Close();
        }

        if (fsw != null)
        {
          fsw.Dispose();
        }

      });

      myThread.Start();
    }
  }

  public class LogEventArgs : System.EventArgs
  {
    public string line { get; set; }
    public MatchCollection matches { get; set; }
    public LogReader.LOG_TYPE type { get; set; }
    public LogEventArgs(string line, MatchCollection matches, LogReader.LOG_TYPE type)
    {
      this.line = line;
      this.matches = matches;
      this.type = type;
    }
  }

  public class ThreadState
  {
    private bool running = true;

    public void stop()
    {
      running = false;
    }

    public bool isRunning()
    {
      return running;
    }
  }
}
