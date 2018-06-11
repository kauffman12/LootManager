using System.Windows.Controls;

namespace LootManager
{
  public class SimpleChatController
  {
    protected CheckBox autoScroll;
    protected RichTextBox richTextBox;

    public SimpleChatController(RichTextBox richTextBox, CheckBox autoScroll)
    {
      this.richTextBox = richTextBox;
      this.autoScroll = autoScroll;
    }

    public void handleEvent(LogEventArgs e)
    {
      appendLine(parseLine(e.line));
    }

    protected string parseLine(string line)
    {
      int firstChat = line.IndexOf(']') + 2;
      return line.Substring(firstChat, line.Length - firstChat);
    }

    protected void appendLine(string line)
    {
      var start = richTextBox.Document.ContentStart;
      var end = richTextBox.Document.ContentEnd;
      int difference = start.GetOffsetToPosition(end);

      // basically empty
      if (difference > 5)
      {
        richTextBox.AppendText("\r");
      }

      richTextBox.AppendText(line);

      if (autoScroll.IsChecked.Value)
      {
        richTextBox.ScrollToEnd();
      }
    }
  }
}