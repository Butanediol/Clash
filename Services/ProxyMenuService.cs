using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClashXW.Models;

namespace ClashXW.Services
{
    public class ProxyMenuService
    {
        private readonly List<MenuItem> _proxyGroupMenus = new List<MenuItem>();

        public IReadOnlyList<MenuItem> ProxyGroupMenus => _proxyGroupMenus.AsReadOnly();

        public void UpdateProxyGroups(ProxiesResponse response, Action<string, string> onProxyNodeSelected)
        {
            _proxyGroupMenus.Clear();

            if (response?.Proxies == null) return;

            var orderedGroups = response.Proxies.TryGetValue("GLOBAL", out var globalGroup) && globalGroup.All != null
                ? globalGroup.All
                    .Select(groupName => response.Proxies.TryGetValue(groupName, out var pg) ? pg : null)
                    .Where(pg => pg != null)
                    .Cast<ProxyNode>()
                    .ToList()
                : response.Proxies.Values.ToList();

            var selectorGroups = orderedGroups
                .Where(p => p.Type.Equals("Selector", StringComparison.OrdinalIgnoreCase))
                .Select(group => CreateProxyGroupMenuItem(group, onProxyNodeSelected))
                .ToList();

            _proxyGroupMenus.AddRange(selectorGroups);
        }

        private MenuItem CreateProxyGroupMenuItem(ProxyNode group, Action<string, string> onProxyNodeSelected)
        {
            var groupMenu = new MenuItem { Header = $"{group.Name} ({group.Now})" };

            var nodeItems = (group.All ?? new List<string>())
                .Select(nodeName => CreateProxyNodeMenuItem(group.Name, nodeName, group.Now ?? "", onProxyNodeSelected))
                .ToList();

            nodeItems.ForEach(item => groupMenu.Items.Add(item));

            return groupMenu;
        }

        private MenuItem CreateProxyNodeMenuItem(string groupName, string nodeName, string currentNode, Action<string, string> onProxyNodeSelected)
        {
            var nodeItem = new MenuItem
            {
                Header = nodeName,
                Tag = new Tuple<string, string>(groupName, nodeName),
                IsChecked = nodeName.Equals(currentNode, StringComparison.OrdinalIgnoreCase)
            };
            nodeItem.Click += (sender, e) => onProxyNodeSelected(groupName, nodeName);
            return nodeItem;
        }
    }
}
