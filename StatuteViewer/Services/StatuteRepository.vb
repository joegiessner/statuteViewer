Imports System.IO
Imports System.Text.Json

Public Class StatuteRepository
    Private ReadOnly _dataDirectory As String
    Private ReadOnly _titleCache As New Dictionary(Of Integer, StatuteNode)()
    Private _index As TitleIndex

    Public Sub New(dataDirectory As String)
        _dataDirectory = dataDirectory
    End Sub

    Public Shared Function ResolveDataDirectory() As String
        Dim candidates As String() = {
            IO.Path.Combine(AppContext.BaseDirectory, "data", "titles"),
            IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "titles"),
            IO.Path.Combine(Directory.GetCurrentDirectory(), "data", "titles"),
            IO.Path.Combine(Directory.GetCurrentDirectory(), "..", "data", "titles")
        }
        For Each candidate In candidates
            Dim full = IO.Path.GetFullPath(candidate)
            If File.Exists(IO.Path.Combine(full, "index.json")) Then
                Return full
            End If
        Next
        Throw New DirectoryNotFoundException(
            "Could not find data/titles/index.json. Build the project so title JSON is copied to the output folder, " &
            "or run from the repository root.")
    End Function

    Public Function LoadIndex() As TitleIndex
        If _index IsNot Nothing Then
            Return _index
        End If
        Dim indexPath = IO.Path.Combine(_dataDirectory, "index.json")
        Dim json = File.ReadAllText(indexPath)
        _index = JsonSerializer.Deserialize(Of TitleIndex)(json, JsonOptions())
        If _index Is Nothing OrElse _index.Titles Is Nothing Then
            Throw New InvalidOperationException("Title index is empty or invalid.")
        End If
        Return _index
    End Function

    Public Function LoadTitle(titleNumber As Integer) As StatuteNode
        Dim cached As StatuteNode = Nothing
        If _titleCache.TryGetValue(titleNumber, cached) Then
            Return cached
        End If

        Dim entry = LoadIndex().Titles.FirstOrDefault(Function(t) t.Number = titleNumber)
        If entry Is Nothing Then
            Throw New ArgumentException($"Unknown title number: {titleNumber}")
        End If

        Dim titlePath = IO.Path.Combine(_dataDirectory, entry.File)
        Dim json = File.ReadAllText(titlePath)
        Dim doc = JsonSerializer.Deserialize(Of TitleDocument)(json, JsonOptions())
        If doc Is Nothing OrElse doc.Root Is Nothing Then
            Throw New InvalidOperationException($"Failed to parse {entry.File}")
        End If

        Dim root = StatuteNode.FromDto(doc.Root)
        _titleCache(titleNumber) = root
        Return root
    End Function

    Public Function Search(query As String, Optional maxResults As Integer = 200) As List(Of SearchHit)
        Dim hits As New List(Of SearchHit)()
        If String.IsNullOrWhiteSpace(query) Then
            Return hits
        End If

        Dim terms = query.Trim().ToLowerInvariant().
            Split({" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
        If terms.Length = 0 Then
            Return hits
        End If

        For Each entry In LoadIndex().Titles
            Dim root = LoadTitle(entry.Number)
            SearchNode(root, terms, hits, maxResults)
            If hits.Count >= maxResults Then
                Exit For
            End If
        Next
        Return hits
    End Function

    Private Shared Sub SearchNode(node As StatuteNode, terms As String(), hits As List(Of SearchHit), maxResults As Integer)
        If hits.Count >= maxResults Then
            Return
        End If

        Dim haystack = (node.Label & " " & If(node.Text, "")).ToLowerInvariant()
        If terms.All(Function(t) haystack.Contains(t)) Then
            hits.Add(New SearchHit With {
                .Node = node,
                .Snippet = BuildSnippet(If(node.Text, node.Label), terms(0))
            })
        End If

        For Each child In node.Children
            SearchNode(child, terms, hits, maxResults)
            If hits.Count >= maxResults Then
                Return
            End If
        Next
    End Sub

    Private Shared Function BuildSnippet(text As String, term As String) As String
        If String.IsNullOrWhiteSpace(text) Then
            Return ""
        End If
        Dim flattened = text.Replace(vbCrLf, " ").Replace(vbLf, " ").Replace(vbCr, " ")
        Do While flattened.Contains("  ")
            flattened = flattened.Replace("  ", " ")
        Loop
        flattened = flattened.Trim()
        Dim idx = flattened.ToLowerInvariant().IndexOf(term.ToLowerInvariant(), StringComparison.Ordinal)
        If idx < 0 Then
            Return If(flattened.Length <= 180, flattened, flattened.Substring(0, 177) & "...")
        End If
        Dim start = Math.Max(0, idx - 60)
        Dim length = Math.Min(180, flattened.Length - start)
        Dim snippet = flattened.Substring(start, length)
        If start > 0 Then snippet = "..." & snippet
        If start + length < flattened.Length Then snippet &= "..."
        Return snippet
    End Function

    Private Shared Function JsonOptions() As JsonSerializerOptions
        Return New JsonSerializerOptions With {
            .PropertyNameCaseInsensitive = True
        }
    End Function
End Class
