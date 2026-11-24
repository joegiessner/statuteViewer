Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input

Class MainWindow
    Private _repository As StatuteRepository
    Private _titlesRoot As New ObservableCollection(Of StatuteNode)()
    Private _searchWorker As BackgroundWorker

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Try
            Dim dataDir = StatuteRepository.ResolveDataDirectory()
            _repository = New StatuteRepository(dataDir)
            Dim index = _repository.LoadIndex()

            _titlesRoot.Clear()
            For Each entry In index.Titles.OrderBy(Function(t) t.Number)
                _titlesRoot.Add(New LazyTitleNode(_repository, entry.Number, entry.Name))
            Next

            StatuteTree.ItemsSource = _titlesRoot
            StatuteTree.AddHandler(TreeViewItem.ExpandedEvent,
                                   New RoutedEventHandler(AddressOf TreeItem_Expanded),
                                   True)

            StatusText.Text = $"Ready — {index.Titles.Count} titles available from local PDF archive (offline)."
            If _titlesRoot.Count > 0 Then
                ShowNode(_titlesRoot(0))
            End If
        Catch ex As Exception
            StatusText.Text = "Failed to load statute data."
            MessageBox.Show(Me,
                            ex.Message,
                            "Delaware Code Viewer",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub TreeItem_Expanded(sender As Object, e As RoutedEventArgs)
        Dim item = TryCast(e.OriginalSource, TreeViewItem)
        If item Is Nothing Then
            Return
        End If
        Dim lazy = TryCast(item.DataContext, LazyTitleNode)
        If lazy IsNot Nothing AndAlso Not lazy.IsLoaded Then
            StatusText.Text = $"Loading Title {lazy.Number}…"
            lazy.EnsureLoaded()
            StatusText.Text = $"Loaded {lazy.Label}"
        End If
    End Sub

    Private Sub StatuteTree_SelectedItemChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Object))
        Dim node = TryCast(e.NewValue, StatuteNode)
        If node Is Nothing Then
            Return
        End If

        Dim lazy = TryCast(node, LazyTitleNode)
        If lazy IsNot Nothing AndAlso Not lazy.IsLoaded Then
            lazy.EnsureLoaded()
        End If

        ShowNode(node)
    End Sub

    Private Sub ShowNode(node As StatuteNode)
        SearchResultsPanel.Visibility = Visibility.Collapsed
        ReaderScroll.Visibility = Visibility.Visible
        ReaderKind.Text = node.KindLabel
        ReaderPath.Text = node.PathLabel
        ReaderText.Text = node.FormatFullText()
        ReaderScroll.ScrollToHome()
        StatusText.Text = node.PathLabel
    End Sub

    Private Sub SearchButton_Click(sender As Object, e As RoutedEventArgs)
        RunSearch()
    End Sub

    Private Sub SearchBox_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            RunSearch()
            e.Handled = True
        End If
    End Sub

    Private Sub ClearSearch_Click(sender As Object, e As RoutedEventArgs)
        SearchBox.Text = ""
        SearchResultsList.ItemsSource = Nothing
        SearchResultsPanel.Visibility = Visibility.Collapsed
        ReaderScroll.Visibility = Visibility.Visible
        StatusText.Text = "Search cleared."
    End Sub

    Private Sub RunSearch()
        Dim query = SearchBox.Text.Trim()
        If query.Length = 0 Then
            MessageBox.Show(Me, "Enter a search term.", "Search", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        If _searchWorker IsNot Nothing AndAlso _searchWorker.IsBusy Then
            Return
        End If

        StatusText.Text = $"Searching for “{query}” across all titles (first run loads each title once)…"
        SearchBox.IsEnabled = False

        _searchWorker = New BackgroundWorker()
        AddHandler _searchWorker.DoWork,
            Sub(s, args)
                args.Result = _repository.Search(CStr(args.Argument), 250)
            End Sub
        AddHandler _searchWorker.RunWorkerCompleted,
            Sub(s, args)
                SearchBox.IsEnabled = True
                If args.Error IsNot Nothing Then
                    StatusText.Text = "Search failed."
                    MessageBox.Show(Me, args.Error.Message, "Search", MessageBoxButton.OK, MessageBoxImage.Error)
                    Return
                End If

                For Each node In _titlesRoot
                    Dim lazy = TryCast(node, LazyTitleNode)
                    If lazy IsNot Nothing AndAlso Not lazy.IsLoaded Then
                        lazy.EnsureLoaded()
                    End If
                Next

                Dim hits = CType(args.Result, List(Of SearchHit))
                ShowSearchResults(query, hits)
            End Sub
        _searchWorker.RunWorkerAsync(query)
    End Sub

    Private Sub ShowSearchResults(query As String, hits As List(Of SearchHit))
        ReaderScroll.Visibility = Visibility.Collapsed
        SearchResultsPanel.Visibility = Visibility.Visible
        SearchResultsList.ItemsSource = hits
        ReaderKind.Text = "Search results"
        ReaderPath.Text = $"Matches for “{query}”"
        If hits.Count = 0 Then
            SearchSummary.Text = "No matches found."
            StatusText.Text = $"No matches for “{query}”."
        Else
            SearchSummary.Text = $"Showing {hits.Count} match(es). Double-click a result to open it."
            StatusText.Text = $"Found {hits.Count} match(es) for “{query}”."
        End If
    End Sub

    Private Sub SearchResultsList_MouseDoubleClick(sender As Object, e As MouseButtonEventArgs)
        OpenSelectedSearchHit()
    End Sub

    Private Sub SearchResultsList_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            OpenSelectedSearchHit()
            e.Handled = True
        End If
    End Sub

    Private Sub OpenSelectedSearchHit()
        Dim hit = TryCast(SearchResultsList.SelectedItem, SearchHit)
        If hit Is Nothing Then
            Return
        End If

        Dim treeNode = FindById(hit.Node.Id)
        If treeNode Is Nothing Then
            treeNode = hit.Node
        End If
        ExpandAndSelect(treeNode)
        ShowNode(treeNode)
    End Sub

    Private Function FindById(id As String) As StatuteNode
        For Each titleNode In _titlesRoot
            Dim found = FindByIdRecursive(titleNode, id)
            If found IsNot Nothing Then
                Return found
            End If
        Next
        Return Nothing
    End Function

    Private Function FindByIdRecursive(node As StatuteNode, id As String) As StatuteNode
        If String.Equals(node.Id, id, StringComparison.Ordinal) Then
            Return node
        End If
        For Each child In node.Children
            Dim found = FindByIdRecursive(child, id)
            If found IsNot Nothing Then
                Return found
            End If
        Next
        Return Nothing
    End Function

    Private Sub ExpandAndSelect(node As StatuteNode)
        Dim stack As New Stack(Of StatuteNode)()
        Dim current = node
        While current IsNot Nothing
            stack.Push(current)
            current = current.Parent
        End While

        Dim container As ItemsControl = StatuteTree
        Dim treeItem As TreeViewItem = Nothing
        While stack.Count > 0
            Dim target = stack.Pop()
            Dim lazy = TryCast(target, LazyTitleNode)
            If lazy IsNot Nothing AndAlso Not lazy.IsLoaded Then
                lazy.EnsureLoaded()
            End If
            container.UpdateLayout()
            treeItem = TryCast(container.ItemContainerGenerator.ContainerFromItem(target), TreeViewItem)
            If treeItem Is Nothing Then
                Exit While
            End If
            treeItem.IsExpanded = True
            treeItem.UpdateLayout()
            container = treeItem
        End While

        If treeItem IsNot Nothing Then
            treeItem.IsSelected = True
            treeItem.BringIntoView()
        End If
    End Sub
End Class
