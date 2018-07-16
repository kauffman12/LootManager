using log4net;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LootManager
{
  public class SimpleChatController
  {
    private static readonly ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    protected CheckBox autoScroll;
    protected RichTextBox richTextBox;

    public SimpleChatController(RichTextBox richTextBox, CheckBox autoScroll)
    {
      this.richTextBox = richTextBox;
      this.autoScroll = autoScroll;
    }

    public void Clear()
    {
      richTextBox.Document.Blocks.Clear();
      richTextBox.Document.Blocks.Add(new Paragraph());
    }

    public void handleEvent(LogEventArgs e)
    {
      appendLine(richTextBox.Document, parseLine(e.line));
    }

    protected string parseLine(string line)
    {
      int firstChat = line.IndexOf(']') + 2;
      return line.Substring(firstChat, line.Length - firstChat);
    }

    protected void appendLine(FlowDocument doc, string line)
    {
      appendLine(doc, line, null);
    }

    protected void appendLine(FlowDocument doc, string line, string highlight)
    {
      Paragraph para = doc.Blocks.FirstBlock as Paragraph;
      if (para != null)
      {
        if (para.Inlines.Count > 0)
        {
          para.Inlines.Add(new LineBreak());
        }

        if (highlight != null)
        {
          int index = line.IndexOf(highlight);
          if (index > -1)
          {
            int end = index + highlight.Length;
            Run one = new Run(line.Substring(0, index));
            Run two = new Run(line.Substring(index, highlight.Length));
            Run three = new Run(line.Substring(end, line.Length - end));

            para.Inlines.Add(one);
            two.Foreground = new SolidColorBrush(Colors.Blue);
            para.Inlines.Add(two);
            para.Inlines.Add(three);
          }
          else
          {
            LOG.Error("Expected Highlight but failed to find:  " + highlight);
            para.Inlines.Add(new Run(line));
          }
        }
        else
        {
          para.Inlines.Add(new Run(line));
        }
      }

      if (autoScroll.IsChecked.Value)
      {
        richTextBox.ScrollToEnd();
      }
    }
  }
}