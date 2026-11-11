/*
 * Copyright 2026 Lydian Scale Software
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System.Collections.ObjectModel;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

using cp2_avalonia.Models;
using cp2_avalonia.Views;

namespace cp2_avalonia.Tests;

/// <summary>
/// E1-E3: the tree item base and the shared TreeView container walk.
/// </summary>
public class TreeViewHelperTests
{
    private class Node : TreeItemBase<Node>
    {
        public Node(Node? parent, string name) : base(parent)
        {
            Name = name;
        }

        public Node Add(string name)
        {
            var child = new Node(this, name);
            Items.Add(child);
            return child;
        }
    }

    // The controls need a theme to get their templates.
    private static void EnsureTheme()
    {
        var styles = Avalonia.Application.Current!.Styles;
        if (!styles.OfType<FluentTheme>().Any()) {
            styles.Add(new FluentTheme());
        }
    }

    private static (Window window, TreeView tree, ObservableCollection<Node> roots) MakeTree()
    {
        EnsureTheme();
        var roots = new ObservableCollection<Node>();
        var tree = new TreeView {
            ItemsSource = roots,
            ItemTemplate = new FuncTreeDataTemplate<Node>(
                (n, _) => new TextBlock { Text = n.Name, Height = 20 },
                n => n.Items),
        };
        var style = new Style(x => x.OfType<TreeViewItem>());
        style.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty,
            new Binding("IsExpanded", BindingMode.TwoWay)));
        tree.Styles.Add(style);
        var window = new Window { Content = tree, Width = 300, Height = 120 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, tree, roots);
    }

    // The headless platform has no render loop to drive layout, so lay out by hand between
    // runs of the dispatcher's queue (the real app's render loop does this).
    private static void Pump(Window window, Func<bool> until)
    {
        for (int i = 0; i < 20 && !until(); i++) {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    [Fact]
    public void ItemBase_StartsExpanded_AndFindsItsPathFromTheRoot()
    {
        var root = new Node(null, "root");
        Node mid = root.Add("mid");
        Node leaf = mid.Add("leaf");
        Assert.True(leaf.IsExpanded);
        Assert.Equal(new[] { root, mid, leaf }, leaf.GetPathFromRoot());
        Assert.Equal(new[] { root }, root.GetPathFromRoot());
        Assert.Null(root.Parent);
        Assert.Same(mid, leaf.Parent);
    }

    [Fact]
    public void ItemBase_RaisesPropertyChanged_ForNameAndExpansion()
    {
        var n = new Node(null, "a");
        var changed = new List<string?>();
        n.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        n.Name = "b";
        n.IsExpanded = false;
        n.IsExpanded = false;       // no change, no event
        Assert.Equal(new[] { "Name", "IsExpanded" }, changed);
    }

    [AvaloniaFact]
    public void FindContainer_IsNullUntilTheTreeIsLaidOut()
    {
        var (window, tree, roots) = MakeTree();
        var root = new Node(null, "root");
        Node leaf = root.Add("a").Add("b");
        roots.Add(root);

        // Just added: no containers yet.  This is the E2 premise.
        Assert.Null(TreeViewHelper.FindContainer(tree, leaf));

        Pump(window, () => TreeViewHelper.FindContainer(tree, leaf) != null);
        Assert.NotNull(TreeViewHelper.FindContainer(tree, leaf));
        window.Close();
    }

    [AvaloniaFact]
    public void BringIntoView_OnAnItemJustAdded_ScrollsOnceLaidOut()
    {
        var (window, tree, roots) = MakeTree();
        var root = new Node(null, "root");
        for (int i = 0; i < 40; i++) {
            root.Add("child " + i);
        }
        Node last = root.Items[^1];
        Node deep = last.Add("deep");
        roots.Add(root);

        TreeViewItem? found = null;
        TreeViewHelper.BringIntoView(tree, deep, tvi => found = tvi);
        Assert.Null(found);                 // nothing realized yet: deferred, not lost

        Pump(window, () => found != null);
        Assert.NotNull(found);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        ScrollViewer sv = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(sv.Offset.Y > 0, "tree did not scroll; offset " + sv.Offset.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void BringIntoView_OnAnItemThatIsNeverAdded_GivesUpQuietly()
    {
        var (window, tree, _) = MakeTree();
        var orphan = new Node(null, "orphan");
        bool called = false;
        TreeViewHelper.BringIntoView(tree, orphan, _ => called = true);
        for (int i = 0; i < 10; i++) {
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(called);
        window.Close();
    }
}
