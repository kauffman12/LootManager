using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LootManager
{
  /// <summary>
  /// Interaction logic for LootAuditWindow.xaml
  /// </summary>
  public partial class LootAuditWindow : Window
  {
    public LootAuditWindow(List<string> selectedTiers, long? time)
    {
      InitializeComponent();
      auditTierComboBox.ItemsSource = DataManager.getTiers();
      auditTierComboBox.SelectedItemsOverride = selectedTiers;
      auditTimeSpinner.Value = time;
    }

    public void load(string player)
    {
      // get tiers filter

      // if data loaded
      if (player != null && tabControl != null && auditTierComboBox.SelectedItemsOverride is List<string> tiers && Resources.Contains("AuditRecordsTemplate"))
      {
        DataGrid dataGrid = null;
        TabItem tabItem = tabControl.Items.Cast<TabItem>().FirstOrDefault(item => player.Equals(item.Header));
        if (tabItem != null)
        {
          dataGrid = ((tabItem.Content as Grid).Children[0] as DataGrid);
          tabControl.SelectedItem = tabItem;
        }
        else
        {
          tabItem = new TabItem();
          Grid grid = new Grid();

          ControlTemplate template = (ControlTemplate)Resources["AuditRecordsTemplate"];
          FrameworkElement elem = template.LoadContent() as FrameworkElement;
          if (elem != null)
          {
            elem.ApplyTemplate();
            grid.Children.Add(elem);
          }

          tabItem.Content = grid;
          tabItem.Header = player;
          tabControl.Items.Add(tabItem);
          tabControl.SelectedItem = tabItem;
          dataGrid = (elem as DataGrid);
        }

        if (dataGrid != null)
        {
          dataGrid.ItemsSource = DataManager.getLootAudit(player, tiers, auditTimeSpinner.Value);
        }
      }
    }

    private void TierComboBox_ItemSelectionChanged(object sender, Xceed.Wpf.Toolkit.Primitives.ItemSelectionChangedEventArgs e)
    {
      // silly workaround
      if (auditTierComboBox.SelectedItemsOverride is List<string> selected)
      {
        auditTierComboBox.SelectedValue = string.Join(",", selected);
      }

      load(getSelectedPlayer());
    }

    private void TimeSpinner_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
      load(getSelectedPlayer());
    }

    private void TabItem_CloseClick(object sender, RoutedEventArgs e)
    {
      if (sender is Image && tabControl.SelectedItem != null)
      {
        tabControl.Items.Remove(tabControl.SelectedItem);
      }
    }

    private string getSelectedPlayer()
    {
      string name = null;
      if (tabControl != null)
      {
        if (tabControl.SelectedItem is TabItem tabItem)
        {
          name = tabItem.Header as string;
        }
      }

      return name;
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      load(getSelectedPlayer());
    }
  }
}
