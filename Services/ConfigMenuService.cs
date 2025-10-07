using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ClashXW.Services
{
    public class ConfigMenuService
    {
        public void UpdateConfigsMenu(MenuItem configMenu, string currentConfigPath, Action<string> onConfigSelected)
        {
            // Remove only config items, keep separator and buttons at the end
            var itemsToRemove = configMenu.Items.OfType<MenuItem>()
                .Where(item => item.Tag is string)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                configMenu.Items.Remove(item);
            }

            var configs = ConfigManager.GetAvailableConfigs();
            int insertIndex = 0;

            foreach (var configPath in configs)
            {
                var menuItem = new MenuItem
                {
                    Header = Path.GetFileName(configPath),
                    Tag = configPath,
                    IsChecked = configPath.Equals(currentConfigPath, StringComparison.OrdinalIgnoreCase)
                };
                menuItem.Click += (sender, e) => onConfigSelected(configPath);
                configMenu.Items.Insert(insertIndex++, menuItem);
            }
        }
    }
}
