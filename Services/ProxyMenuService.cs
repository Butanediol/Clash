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

        public void UpdateProxyGroups(ProxiesResponse response, Action<string, string> onProxyNodeSelected, Action<string> onTestGroupLatency)
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
                .Select(group => CreateProxyGroupMenuItem(group, response.Proxies, onProxyNodeSelected, onTestGroupLatency))
                .ToList();

            _proxyGroupMenus.AddRange(selectorGroups);
        }

        private MenuItem CreateProxyGroupMenuItem(ProxyNode group, Dictionary<string, ProxyNode> allProxies, Action<string, string> onProxyNodeSelected, Action<string> onTestGroupLatency)
        {
            var groupLatency = GetLatestLatency(group);
            var groupHeader = groupLatency.HasValue ? $"{group.Name} ({group.Now})  [{groupLatency}ms]" : $"{group.Name} ({group.Now})";
            var groupMenu = new MenuItem { Header = groupHeader };

            // Add "Test Latency" button as the first item
            var testLatencyItem = new MenuItem
            {
                Header = "Test Latency",
                Tag = group.Name
            };
            testLatencyItem.Click += (sender, e) => onTestGroupLatency(group.Name);
            groupMenu.Items.Add(testLatencyItem);

            // Add separator
            groupMenu.Items.Add(new Separator());

            var nodeItems = (group.All ?? new List<string>())
                .Select(nodeName => CreateProxyNodeMenuItem(group.Name, nodeName, group.Now ?? "", allProxies, onProxyNodeSelected))
                .ToList();

            nodeItems.ForEach(item => groupMenu.Items.Add(item));

            return groupMenu;
        }

        private MenuItem CreateProxyNodeMenuItem(string groupName, string nodeName, string currentNode, Dictionary<string, ProxyNode> allProxies, Action<string, string> onProxyNodeSelected)
        {
            var nodeLatency = GetNodeLatency(nodeName, allProxies);
            var nodeHeader = nodeLatency.HasValue ? $"{nodeName}  [{nodeLatency}ms]" : nodeName;

            var nodeItem = new MenuItem
            {
                Header = nodeHeader,
                Tag = new Tuple<string, string>(groupName, nodeName),
                IsChecked = nodeName.Equals(currentNode, StringComparison.OrdinalIgnoreCase)
            };
            nodeItem.Click += (sender, e) => onProxyNodeSelected(groupName, nodeName);
            return nodeItem;
        }

        private int? GetLatestLatency(ProxyNode proxy)
        {
            return proxy.History?.LastOrDefault()?.Delay;
        }

        private int? GetNodeLatency(string nodeName, Dictionary<string, ProxyNode> allProxies)
        {
            if (allProxies.TryGetValue(nodeName, out var node))
            {
                return GetLatestLatency(node);
            }
            return null;
        }
    }
}
